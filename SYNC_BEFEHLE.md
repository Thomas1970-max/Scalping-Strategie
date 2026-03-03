# Sync Befehle - Multi-PC Setup

## PC Workflow (Arbeiten auf PC)

```bash
# Zum Projektverzeichnis navigieren (PC)
cd "c:\Users\User\source\repos\Scalping-Strategie"

# Änderungen committen
git add .
git commit -m "Feature: Beschreibung der Änderungen"

# Zum recover/from-stash Branch pushen
git push origin recover/from-stash
```

## Laptop Workflow (Weiterarbeiten auf Laptop)

```bash
# Zum Projektverzeichnis navigieren (Laptop)
cd "C:\Users\tleue\OneDrive\Projekte\Scalping-Strategie"

# Änderungen vom PC pullen
git pull origin recover/from-stash

# Nach deinen Änderungen auf dem Laptop:
git add .
git commit -m "Feature: Laptop-Änderungen"
git push origin recover/from-stash
```

## Zurück auf PC

```bash
# Laptop-Änderungen holen
git pull origin recover/from-stash
```

## Status prüfen

```bash
# Aktuellen Branch und Status anzeigen
git status
git branch

# Letzte Commits anzeigen
git log --oneline -5
```

## Wichtige Regeln

1. **Immer auf `recover/from-stash` Branch bleiben**
2. **Vor Gerätewechsel**: Immer `git push origin recover/from-stash`
3. **Nach Gerätewechsel**: Immer `git pull origin recover/from-stash`
4. **OneDrive synchronisiert automatisch** im Hintergrund

## Bei Konflikten

```bash
# Konflikte auflösen
git pull origin recover/from-stash
# Konflikte manuell in den Dateien lösen
git add .
git commit -m "Resolve: Merge-Konflikt behoben"
git push origin recover/from-stash
```

## Quick Copy - PC → Laptop

**PC:**
```bash
git add . && git commit -m "Update: PC Änderungen" && git push origin recover/from-stash
```

**Laptop:**
```bash
git pull origin recover/from-stash
```
