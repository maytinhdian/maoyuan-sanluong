; Bộ cài Windows cho Display Board (Inno Setup 6).
; Build: chạy tools/publish.ps1 trước, rồi:  iscc /DAppVersion=1.0.0 tools/installer/DisplayBoard.iss
; Kết quả: dist/DisplayBoard-Setup-<version>.exe

#ifndef AppVersion
  #define AppVersion "2.0.0"
#endif

[Setup]
AppId={{6F4B2C1E-8A3D-4E5F-9B7C-2D1A0E3F4B5C}
AppName=Display Board
AppVersion={#AppVersion}
AppPublisher=Công ty TNHH Giải Pháp Sáng Tạo TMT Việt Nam
AppPublisherURL=https://maytinhdian.com
AppSupportURL=https://maytinhdian.com
DefaultDirName={autopf}\DisplayBoard
DefaultGroupName=Display Board
DisableProgramGroupPage=yes
OutputDir=..\..\dist
OutputBaseFilename=DisplayBoard-Setup-{#AppVersion}
SetupIconFile=..\..\src\DisplayBoard.App\Assets\app.ico
UninstallDisplayIcon={app}\DisplayBoard.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequiredOverridesAllowed=dialog
CloseApplications=yes

[Tasks]
Name: "desktopicon"; Description: "Tạo biểu tượng ngoài Desktop"; GroupDescription: "Tuỳ chọn:"
Name: "autostart"; Description: "Tự chạy khi khởi động Windows (cho máy nối TV hoặc máy chủ)"; GroupDescription: "Tuỳ chọn:"; Flags: unchecked
; Bản 2.x phát số liệu cho TV qua mạng LAN (cổng mặc định 5080). Cần quyền admin để mở tường lửa.
Name: "firewall"; Description: "Cho phép TV trong mạng LAN kết nối (mở Windows Firewall)"; GroupDescription: "Mạng LAN:"; Check: IsAdminInstallMode

[Files]
Source: "..\..\dist\DisplayBoard\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Display Board"; Filename: "{app}\DisplayBoard.exe"
Name: "{group}\Hướng dẫn"; Filename: "{app}\HUONG_DAN.txt"
Name: "{group}\File mẫu"; Filename: "{app}\samples"
Name: "{group}\Gỡ cài đặt Display Board"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Display Board"; Filename: "{app}\DisplayBoard.exe"; Tasks: desktopicon
Name: "{userstartup}\Display Board"; Filename: "{app}\DisplayBoard.exe"; Tasks: autostart

[Run]
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""Display Board"""; Flags: runhidden; Tasks: firewall
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""Display Board"" dir=in action=allow program=""{app}\DisplayBoard.exe"" enable=yes profile=private,domain"; Flags: runhidden; Tasks: firewall
Filename: "{app}\DisplayBoard.exe"; Description: "Mở Display Board"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""Display Board"""; Flags: runhidden; RunOnceId: "DelFirewallRule"
