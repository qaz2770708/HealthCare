"""Create an online SQLite snapshot plus Data Protection keys in a new timestamped folder."""
import datetime
import shutil
import sqlite3
import sys
from pathlib import Path

if len(sys.argv) != 3:
    raise SystemExit('Usage: python tools/backup.py <data-directory> <backup-parent>')
source = Path(sys.argv[1]).resolve()
db_path = source / 'clinic.db'
if not db_path.is_file(): raise SystemExit('Database does not exist: ' + str(db_path))
destination = Path(sys.argv[2]).resolve() / datetime.datetime.now().strftime('clinic-%Y%m%d-%H%M%S-%f')
destination.mkdir(parents=True, exist_ok=False)
with sqlite3.connect(db_path.as_uri() + '?mode=ro', uri=True) as original:
    with sqlite3.connect(destination / 'clinic.db') as backup:
        original.backup(backup)
        result = backup.execute('PRAGMA integrity_check').fetchone()[0]
        if result != 'ok': raise RuntimeError(result)
if (source/'keys').exists(): shutil.copytree(source/'keys',destination/'keys')
print('Backup completed:', destination)
