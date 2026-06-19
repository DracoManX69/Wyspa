# Wyspa Troubleshooting

This guide covers common install, setup, microphone, transcription, and uninstall problems.

## AI Disclosure

Wyspa and this troubleshooting guide were written and produced with AI assistance from Codex. Validate fixes before using Wyspa in sensitive workflows.

## Installer Says .NET Is Missing

Wyspa's lightweight installer requires Microsoft .NET 10 Desktop Runtime x64.

If setup says .NET is missing:

1. Let the installer open the Microsoft runtime page or installer.
2. Install the .NET 10 Desktop Runtime x64.
3. Run `WyspaSetup-0.4.6-win-x64.exe` again.

The Wyspa installer does not currently install .NET silently inside the wizard.

## Installer Does Not Finish

Try these steps:

- close any running Wyspa tray icon;
- check whether `Wyspa.exe` is still running in Task Manager;
- run the installer again;
- install into the default per-user folder;
- make sure Windows Defender or another security tool is not blocking the setup file.

The installer does not require administrator privileges for the default per-user install.

## Uninstall Leaves Files Behind

This usually means `Wyspa.exe` was still running and Windows kept app files locked.

The current uninstaller asks Wyspa to quit before removing files and force-closes it if needed. If you still see leftover files:

1. Open Task Manager.
2. End any `Wyspa.exe` process.
3. Delete the install folder manually:

```text
%LocalAppData%\Programs\Wyspa
```

Your settings and API key are stored separately in `%AppData%\Wyspa` and are only removed if you choose not to keep data during uninstall.

## Invalid Groq API Key

Open Wyspa Settings > Groq.

- Paste the key again.
- Make sure there are no extra spaces before or after it.
- Click Test connection.
- Confirm the key is active in the Groq Console.

Wyspa checks the key by calling Groq's models endpoint and looking for transcription model availability.

## Groq Model Not Available

If Wyspa connects but says `whisper-large-v3-turbo` is not listed, the key may not have access to that model.

Try:

- creating a new Groq API key;
- checking your Groq account access;
- waiting and testing again if Groq has a temporary issue.

## Network Or Rate Limit Error

Check your internet connection and try Test connection again.

Groq errors may be temporary. Rate limit errors usually clear after waiting a short time.

## Microphone Is Not Detected

Check:

- Windows Settings > Privacy & security > Microphone;
- microphone access for desktop apps;
- the selected input device in Wyspa Settings > Input;
- whether the Windows input meter moves while speaking;
- whether another app has exclusive control of the microphone.

After changing devices, refresh or restart Wyspa.

## Transcript Is Always "Thank you."

This usually means Groq received silence or the wrong microphone input.

Try:

- choose the correct microphone in Settings > Input;
- speak while watching the input meter;
- lower the AutoCapture threshold if using AutoCapture;
- make sure Windows microphone permissions are enabled;
- disable noise suppression in other audio tools if it is cutting off speech.

Wyspa includes a silent-audio guard, but very quiet or misrouted recordings can still produce poor transcripts.

## AutoCapture Starts And Stops Too Often

AutoCapture depends on microphone levels.

Try:

- raise the AutoCapture threshold if background noise triggers recording;
- lower the threshold if speech is not detected;
- increase silence duration if recording stops between words;
- choose a specific microphone rather than Windows default;
- test in a quieter room.

## Hotkey Does Not Register

The default hotkey is:

```text
Ctrl+Alt+Space
```

If recording a shortcut fails:

- try a different combination such as `Ctrl+Shift+F8`;
- avoid hotkeys already owned by another app;
- configure macro pads to emit normal key-down/key-up events;
- use macro keys such as `F13`-`F24` where possible.

Combos such as `Ctrl+F4` should be supported.

## Hold To Talk Does Not Stop

Hold to talk needs Wyspa to receive the shortcut key release event.

If it keeps listening:

- switch to Toggle mode;
- configure the macro pad to send both key-down and key-up;
- use a dedicated key such as `F13`;
- avoid keyboard software that swallows key release events.

## Text Does Not Insert

Wyspa normally inserts text by temporarily using the clipboard and sending Ctrl+V.

If text does not insert:

- click into the target text field before dictating;
- try Notepad to confirm insertion works in a simple app;
- switch insertion mode from Paste to Type;
- check whether the target app blocks simulated paste/type input;
- manually paste if Wyspa leaves the transcript on the clipboard.

Secure fields, elevated/admin windows, and some games or remote desktops may block insertion.

## Clipboard Looks Different After Dictation

In paste mode, Wyspa attempts to restore the previous clipboard after pasting. Some clipboard formats or busy clipboard managers can prevent perfect restoration.

If this bothers you, switch to Type insertion mode.

## Overlay Is Too Visible Or Too Faint

Open Settings > Behavior and adjust Recording overlay transparency.

- Lower values make the background more transparent.
- Higher values make the overlay background more opaque.

The waveform remains visible independently of the background opacity.

## Start With Windows Does Not Work

Start with Windows uses the current user's Run registry key and launches Wyspa with `--minimized`.

Try:

- toggle Start with Windows off and on again in Wyspa;
- check the tray menu setting matches the in-app setting;
- make sure Wyspa has not been moved after enabling startup;
- reinstall Wyspa if the install path changed.

## App Will Not Open

Check:

```text
%AppData%\Wyspa\crash.log
```

Also confirm the .NET 10 Desktop Runtime x64 is installed.

## Need A Clean Reset

1. Quit Wyspa from the tray.
2. Uninstall Wyspa from Windows Settings > Apps.
3. Choose not to keep data.
4. Confirm this folder is gone:

```text
%AppData%\Wyspa
```

5. Reinstall Wyspa.

This removes settings and the encrypted Groq API key, so you will need to add your key again.
