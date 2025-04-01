REM Compile the LaTeX file
pdflatex PRo3D_ShortUserManual.tex

REM Run pdflatex again to ensure references are updated
pdflatex PRo3D_ShortUserManual.tex

REM Notify the user of completion
echo Compilation complete. Check for PRo3D_ShortUserManual.pdf in the current directory.
pause