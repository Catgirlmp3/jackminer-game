Option Explicit

Dim shell, fso, appPath, answer, command
Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")

appPath = fso.BuildPath(fso.GetParentFolderName(WScript.ScriptFullName), "JackMiner.exe")

answer = MsgBox("JACKMINER — BETA SOFTWARE" & vbCrLf & vbCrLf & _
    "This is experimental software. It includes an optional XMRig mining component, " & _
    "which may trigger Microsoft Defender detections." & vbCrLf & vbCrLf & _
    "Only continue if you trust this build. Administrator permission will be requested " & _
    "to support CPU mining features. Mining remains optional." & vbCrLf & vbCrLf & _
    "Continue to JackMiner?", vbExclamation + vbYesNo + vbDefaultButton2, "JackMiner Beta Notice")

If answer <> vbYes Then WScript.Quit 0

If Not fso.FileExists(appPath) Then
    MsgBox "JackMiner.exe was not found beside this launcher." & vbCrLf & _
        "Please use Download / Repair Required Files or reinstall the beta.", vbCritical, "JackMiner"
    WScript.Quit 1
End If

command = "powershell.exe -NoProfile -ExecutionPolicy Bypass -Command " & _
    """Start-Process -FilePath '" & Replace(appPath, "'", "''") & "' -Verb RunAs"""
shell.Run command, 0, False
