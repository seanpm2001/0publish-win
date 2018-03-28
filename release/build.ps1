$ErrorActionPreference = "Stop"
pushd $(Split-Path -Path $MyInvocation.MyCommand.Definition -Parent)

# Ensure 0install is in the PATH
if (!(Get-Command 0install -ErrorAction SilentlyContinue)) {
    mkdir -Force "$env:TEMP\zero-install" | Out-Null
    Invoke-WebRequest "https://0install.de/files/0install.exe" -OutFile "$env:TEMP\zero-install\0install.exe"
    $env:PATH = "$env:TEMP\zero-install;$env:PATH"
}

rm -Force ..\build\Release\*.xml
rm -Force ..\build\Release\*.pdb

cmd /c "0install run --batch http://0install.net/tools/0template.xml ZeroInstall_Tools.xml.template version=$(Get-Content ..\VERSION) 2>&1" # Redirect stderr to stdout

popd
