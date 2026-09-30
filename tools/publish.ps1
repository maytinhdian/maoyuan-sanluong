# Đóng gói app thành một file DisplayBoard.exe chạy được trên Windows 10/11 64-bit, không cần cài .NET.
# Chạy từ thư mục gốc repo:  powershell -ExecutionPolicy Bypass -File tools/publish.ps1
$ErrorActionPreference = "Stop"
$out = "dist/DisplayBoard"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish src/DisplayBoard.App -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -o $out
Copy-Item samples "$out/samples" -Recurse
Copy-Item tools/HUONG_DAN.txt $out
Compress-Archive -Path "$out/*" -DestinationPath "dist/DisplayBoard-win-x64.zip" -Force
Write-Host "Xong: dist/DisplayBoard-win-x64.zip"
