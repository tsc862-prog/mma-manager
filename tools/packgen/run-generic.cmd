@echo off
rem Daily-credit generic batch launcher — sets the pack env vars so a manual
rem run can never land in the wrong pack. Usage:  run-generic.cmd [target]
rem (target = TOTAL fighters to reach, default 1200; resumable, stops on quota)
set MMA_PACK_DIR=e:\MMA Manager\generated-pack-generic
set MMA_STYLE=realistic
set MMA_PACK_PORT=8768
set TARGET=%1
if "%TARGET%"=="" set TARGET=1200
python "%~dp0zgeneric.py" %TARGET%
rem `pause` removed: it hangs forever when run non-interactively (background
rem agent runs). If double-clicking, launch from an already-open terminal to
rem keep the window/output after it finishes.
