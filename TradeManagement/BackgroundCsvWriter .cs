using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Linq;
using System.Text.Json.Nodes;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Utils.Common.Logging;

namespace MyNamespace.Strategies.TradeManagement
{
    public class BackgroundCsvWriter : IDisposable
    {
        readonly BlockingCollection<string> _queue = new BlockingCollection<string>(new ConcurrentQueue<string>());
        readonly Task _writerTask;
        readonly string _path;
        readonly string _header;
        readonly CancellationTokenSource _cts = new CancellationTokenSource();
        // headerWritten accessed from multiple threads -> protect with lock
        bool _headerWritten;
        readonly object _headerLock = new object();
        // thread-local random for jitter
        static readonly ThreadLocal<Random> _random = new ThreadLocal<Random>(() => new Random(unchecked(Environment.TickCount * 31 + Thread.CurrentThread.ManagedThreadId)));

        public int QueueCount => _queue.Count;

        public BackgroundCsvWriter(string path, string header)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
            _header = header ?? string.Empty;

            try
            {
                // safe check whether file exists and has content
                _headerWritten = File.Exists(_path) && new FileInfo(_path).Length > 0;
            }
            catch (Exception ex)
            {
                // defensiv: falls kein Zugriff möglich ist, treat as not written and continue
                this.LogWarn($"[CSV_WRITER] Could not determine existing file/header state for '{_path}': {ex.Message}. Will attempt to write header on first write.");
                _headerWritten = false;
            }

            _writerTask = Task.Run(() => WriterLoop(_cts.Token));

            _writerTask.ContinueWith(t =>
            {
                if (t.IsFaulted)
                    this.LogInfo($"[CSV_WRITER] writer task fehlerhaft: {t.Exception?.Flatten().InnerException ?? t.Exception}");
                else if (t.IsCanceled)
                    this.LogInfo("[CSV_WRITER] writer task canceled.");
                else
                    this.LogInfo("[CSV_WRITER] writer task completed.");
            }, TaskContinuationOptions.ExecuteSynchronously);
        }

        public void EnqueueLine(string csvLine)
        {
            if (string.IsNullOrWhiteSpace(csvLine))
                return; // ignore empty lines

            // if adding has been completed, ignore (no exception)
            if (_queue.IsAddingCompleted)
            {
                this.LogWarn("[CSV_WRITER] Attempt to enqueue after CompleteAdding - line dropped.");
                return;
            }

            try
            {
                _queue.Add(csvLine);
            }
            catch (InvalidOperationException)
            {
                // Collection has been marked as complete for adding
                this.LogWarn("[CSV_WRITER] Enqueue failed: adding completed (line dropped).");
            }
            catch (Exception ex)
            {
                this.LogError($"[CSV_WRITER] EnqueueLine unexpected exception: {ex}");
            }
        }

        async Task WriterLoop(CancellationToken ct)
        {
            this.LogInfo($"[CSV_WRITER] WriterLoop starting for path {_path}; headerWritten={_headerWritten}");
            const int maxRetries = 8;
            const int baseDelayMs = 50;

            try
            {
                var batch = new List<string>(capacity: 128);

                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        string first;
                        // block until a line is available or cancellation requested
                        if (!_queue.TryTake(out first, 500, ct))
                            continue;

                        if (!string.IsNullOrEmpty(first))
                            batch.Add(first);

                        // collect a few more lines to amortize IO
                        while (batch.Count < 128 && _queue.TryTake(out var more, 0))
                        {
                            if (!string.IsNullOrEmpty(more))
                                batch.Add(more);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    if (batch.Count == 0) continue;

                    int attempt = 0;
                    bool success = false;
                    while (!success && attempt < maxRetries && !ct.IsCancellationRequested)
                    {
                        attempt++;
                        try
                        {
                            // use append and allow other processes to read/write/delete
                            using (var fs = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                            using (var sw = new StreamWriter(fs, new UTF8Encoding(false)) { AutoFlush = true })
                            {
                                // If header not yet written and file is empty (or we previously couldn't detect), write header
                                bool needToWriteHeader = false;
                                lock (_headerLock)
                                {
                                    if (!_headerWritten && fs.Length == 0)
                                    {
                                        needToWriteHeader = true;
                                        // mark true here to avoid races writing header twice
                                        _headerWritten = true;
                                    }
                                }

                                if (needToWriteHeader)
                                {
                                    if (!string.IsNullOrEmpty(_header))
                                    {
                                        await sw.WriteLineAsync(_header).ConfigureAwait(false);
                                        await sw.FlushAsync().ConfigureAwait(false);
                                        this.LogInfo($"[CSV_WRITER] Header written to {_path}");
                                    }
                                }

                                foreach (var line in batch)
                                {
                                    await sw.WriteLineAsync(line).ConfigureAwait(false);
                                }
                                await sw.FlushAsync().ConfigureAwait(false);
                            }

                            success = true;
                            this.LogDebug($"[CSV_WRITER] Wrote {batch.Count} lines to {_path} (attempt {attempt})");
                        }
                        catch (IOException ioEx)
                        {
                            this.LogWarn($"[CSV_WRITER] IO attempt {attempt}/{maxRetries} failed writing to '{_path}': {ioEx.Message}");
                            var delay = baseDelayMs * (1 << Math.Min(attempt - 1, 6));
                            var jitter = _random.Value.Next(0, Math.Min(100, delay));
                            try { await Task.Delay(delay + jitter, ct).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
                        }
                        catch (UnauthorizedAccessException uaEx)
                        {
                            this.LogWarn($"[CSV_WRITER] UnauthorizedAccess while writing to '{_path}': {uaEx.Message}. Aborting attempts.");
                            break;
                        }
                        catch (Exception ex)
                        {
                            this.LogError($"[CSV_WRITER] Unexpected error while writing to '{_path}': {ex}");
                            break;
                        }
                    }

                    if (!success)
                    {
                        this.LogError($"[CSV_WRITER] Failed to write batch of {batch.Count} lines to '{_path}' after {maxRetries} attempts. Lines will be dropped or persisted to .failed.");
                        try
                        {
                            var dl = _path + ".failed";
                            File.AppendAllLines(dl, batch, new UTF8Encoding(false));
                            this.LogInfo($"[CSV_WRITER] Persisted failed batch to {dl}");
                        }
                        catch (Exception exDl)
                        {
                            this.LogWarn($"[CSV_WRITER] Could not persist failed batch to disk: {exDl}");
                        }
                    }

                    batch.Clear();
                } // while
            }
            catch (Exception ex)
            {
                this.LogError($"[CSV_WRITER] WriterLoop fatal exception: {ex}");
                throw;
            }
            finally
            {
                this.LogInfo($"[CSV_WRITER] WriterLoop exiting for path {_path}");
            }
        }

        public void Dispose()
        {
            // Signal no more adds
            try
            {
                _queue.CompleteAdding();
            }
            catch (Exception) { /* swallow */ }

            // ask writer thread to stop if it's blocked in waits
            try
            {
                if (!_cts.IsCancellationRequested)
                    _cts.Cancel();
            }
            catch { /* swallow */ }

            try
            {
                // Wait for worker to finish writing remaining lines
                if (_writerTask != null && !_writerTask.IsCompleted)
                {
                    // Wait with a timeout, but prefer graceful completion
                    if (!_writerTask.Wait(5000))
                    {
                        this.LogWarn("[CSV_WRITER] writer task did not finish in time; cancelling.");
                        try { _cts.Cancel(); } catch { }
                        _writerTask.Wait(2000);
                    }
                }
            }
            catch (AggregateException) { /* swallow */ }
            catch (Exception ex) { this.LogWarn($"[CSV_WRITER] Exception while waiting writer task: {ex.Message}"); }
            finally
            {
                try { _cts.Dispose(); } catch { }
                try { _queue.Dispose(); } catch { }
            }
        }
    }
}


