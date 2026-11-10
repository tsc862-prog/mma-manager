@echo off
rem One-shot scheduled batch launcher (created by Claude for the ~17:21 quota
rem reset on 2026-08-07). Sets the generic-pack env and runs zgeneric with the
rem new variety fixes; resumable, stops on quota. Log -> scheduled-batch.log
set MMA_PACK_DIR=e:\MMA Manager\generated-pack-generic
set MMA_STYLE=realistic
set MMA_PACK_PORT=8768
cd /d "e:\MMA Manager\tools\packgen"
"C:\Users\timca\AppData\Local\Microsoft\WindowsApps\python.exe" zgeneric.py 3800 > "e:\MMA Manager\tools\packgen\scheduled-batch.log" 2>&1
