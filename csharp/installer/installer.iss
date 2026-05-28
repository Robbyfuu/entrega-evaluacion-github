; Inno Setup script para Entrega de Evaluacion a GitHub
; Compila con: ISCC.exe installer.iss
; El EXE viene de ..\..\build-output\EntregaEvaluacion.exe (lo genera dotnet publish)

#define MyAppName "Entrega de Evaluacion a GitHub"
#define MyAppVersion "2.0.0"
#define MyAppPublisher "DUOC UC - FPY1101"
#define MyAppExeName "EntregaEvaluacion.exe"

[Setup]
AppId={{B7E3A1F2-9C4D-4E8A-A1B2-3C4D5E6F7A8B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\EntregaEvaluacion
DefaultGroupName=Entrega Evaluacion
DisableProgramGroupPage=yes
; Instala sin requerir admin (per-user)
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=Output
OutputBaseFilename=EntregaEvaluacion-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear acceso directo en el Escritorio"; GroupDescription: "Accesos directos:"
Name: "autostart"; Description: "Iniciar automaticamente al encender el PC (recomendado para aulas)"; GroupDescription: "Inicio automatico:"; Flags: unchecked

[Files]
Source: "..\..\build-output\EntregaEvaluacion.exe"; DestDir: "{app}"; Flags: ignoreversion
; Incluir la guia PDF si existe
Source: "..\..\docs\Guia-Alumno.pdf"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
Name: "{group}\Entrega Evaluacion"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Guia del Alumno (PDF)"; Filename: "{app}\Guia-Alumno.pdf"; Check: FileExists(ExpandConstant('{app}\Guia-Alumno.pdf'))
Name: "{autodesktop}\Entrega Evaluacion"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Auto-arranque si el usuario marco la tarea
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "EntregaEvaluacion"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Abrir Entrega de Evaluacion ahora"; Flags: nowait postinstall skipifsilent
