# Multi-PC Setup mit OneDrive + Git

## Konfiguration

### Aktuelle Setup
- **Remote Repository**: GitHub (origin)
- **Lokaler Speicher PC**: `c:\Users\User\source\repos\Scalping-Strategie`
- **Lokaler Speicher Laptop**: `C:\Users\DeinUsername\OneDrive\Projekte\Scalping-Strategie`
- **Cloud Backup**: OneDrive Sync

### Sync-Workflow zwischen PC und Laptop

#### 1. Initial Setup (auf beiden Geräten)
```bash
# Repository klonen
git clone https://github.com/Thomas1970-max/Scalping-Strategie.git
cd Scalping-Strategie

# Git Konfiguration
git config user.name "Dein Name"
git config user.email "deine.email@example.com"
```

#### 2. Täglicher Workflow
**Auf PC arbeiten:**
```bash
# 1. Änderungen committen
git add .
git commit -m "Feature: Beschreibung der Änderungen"

# 2. Zu GitHub pushen
git push origin recover/from-stash

# 3. OneDrive synchronisiert automatisch
```

**Auf Laptop weiterarbeiten:**
```bash
# 1. Von GitHub pullen
git pull origin recover/from-stash

# 2. Änderungen committen
git add .
git commit -m "Feature: Weitere Änderungen"

# 3. Zurück zu GitHub pushen
git push origin recover/from-stash
```

#### 3. Konfliktlösung
Bei Merge-Konflikten:
```bash
git pull origin recover/from-stash
# Konflikte manuell lösen
git add .
git commit -m "Resolve: Merge-Konflikt behoben"
git push origin recover/from-stash
```

## OneDrive Integration

### Automatischer Backup
- Das gesamte Repository wird über OneDrive synchronisiert
- Bei Problemen mit GitHub kann das Repository aus OneDrive wiederhergestellt werden

### Ausgeschlossene Dateien
- `.vscode/` - IDE-spezifische Einstellungen
- `*.tmp`, `*.log` - Temporäre Dateien
- `.DS_Store`, `Thumbs.db` - Systemdateien

## Best Practices

1. **Regelmäßig committen**: Nach jeder größeren Änderung
2. **Vor Gerätewechsel**: Immer `git push` ausführen
3. **Nach Gerätewechsel**: Immer `git pull` ausführen
4. **Branche beachten**: Auf `recover/from-stash` bleiben

## Notfall-Wiederherstellung

Falls GitHub nicht erreichbar:
1. OneDrive Sync prüfen
2. Repository aus OneDrive kopieren
3. `git status` prüfen
4. Änderungen committen und später pushen
