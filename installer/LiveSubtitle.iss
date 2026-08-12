#define MyAppName "LiveSubtitle"
#define MyAppVersion "0.1.0"
#define MyAppExeName "LiveSubtitle.exe"

[Setup]
AppId={{E801857B-0758-4CA5-AC2D-A1267CF4DA7B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=LiveSubtitle
VersionInfoVersion={#MyAppVersion}.0
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir=..\artifacts
OutputBaseFilename=LiveSubtitle-{#MyAppVersion}-win-x64-setup
SetupIconFile=..\src\LiveSubtitle.App\Assets\LiveSubtitle.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
UsePreviousAppDir=yes
UsePreviousTasks=yes
ShowLanguageDialog=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "..\artifacts\LiveSubtitle.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "LiveSubtitle"; Flags: uninsdeletevalue dontcreatekey

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
var
  DeleteAllData: Boolean;

function InitializeUninstall(): Boolean;
var
  Form: TSetupForm;
  Heading, Detail: TNewStaticText;
  DeleteDataCheckBox: TNewCheckBox;
  ContinueButton, CancelButton: TNewButton;
  ButtonWidth: Integer;
begin
  DeleteAllData := False;
  Result := True;
  if UninstallSilent then
    Exit;

  Form := CreateCustomForm(ScaleX(500), ScaleY(205), False, True);
  try
    Form.Caption := 'Uninstall LiveSubtitle';

    Heading := TNewStaticText.Create(Form);
    Heading.Parent := Form;
    Heading.Left := ScaleX(18);
    Heading.Top := ScaleY(18);
    Heading.Width := Form.ClientWidth - ScaleX(36);
    Heading.Caption := 'Keep or delete your local data';
    Heading.Font.Style := [fsBold];
    Heading.Font.Size := 11;

    Detail := TNewStaticText.Create(Form);
    Detail.Parent := Form;
    Detail.Left := ScaleX(18);
    Detail.Top := ScaleY(50);
    Detail.Width := Form.ClientWidth - ScaleX(36);
    Detail.Height := ScaleY(52);
    Detail.AutoSize := False;
    Detail.WordWrap := True;
    Detail.Caption :=
      'By default, uninstall removes only the application. Your settings, downloaded models, ' +
      'sanitized logs, and saved sessions remain under Local AppData.';

    DeleteDataCheckBox := TNewCheckBox.Create(Form);
    DeleteDataCheckBox.Parent := Form;
    DeleteDataCheckBox.Left := ScaleX(18);
    DeleteDataCheckBox.Top := ScaleY(112);
    DeleteDataCheckBox.Width := Form.ClientWidth - ScaleX(36);
    DeleteDataCheckBox.Caption := 'Delete all LiveSubtitle data';
    DeleteDataCheckBox.Checked := False;

    ContinueButton := TNewButton.Create(Form);
    ContinueButton.Parent := Form;
    ContinueButton.Caption := 'Continue';
    ContinueButton.Top := Form.ClientHeight - ScaleY(33);
    ContinueButton.ModalResult := mrOk;
    ContinueButton.Default := True;

    CancelButton := TNewButton.Create(Form);
    CancelButton.Parent := Form;
    CancelButton.Caption := 'Cancel';
    CancelButton.Top := ContinueButton.Top;
    CancelButton.ModalResult := mrCancel;
    CancelButton.Cancel := True;

    ButtonWidth := Form.CalculateButtonWidth([ContinueButton.Caption, CancelButton.Caption]);
    ContinueButton.Width := ButtonWidth;
    CancelButton.Width := ButtonWidth;
    CancelButton.Left := Form.ClientWidth - ScaleX(18) - CancelButton.Width;
    ContinueButton.Left := CancelButton.Left - ScaleX(8) - ContinueButton.Width;
    Form.ActiveControl := ContinueButton;

    if Form.ShowModal() = mrOk then
      DeleteAllData := DeleteDataCheckBox.Checked
    else
      Result := False;
  finally
    Form.Free();
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and DeleteAllData then
    DelTree(ExpandConstant('{localappdata}\LiveSubtitle'), True, True, True);
end;
