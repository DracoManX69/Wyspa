# Wyspa Troubleshooting

This guide covers common install, setup, microphone, transcription, and uninstall problems.

## AI Disclosure

Wyspa and this troubleshooting guide were written and produced with AI assistance from Codex. Validate fixes before using Wyspa in sensitive workflows.

## Installer Says .NET Is Missing

Wyspa's lightweight installer requires Microsoft .NET 10 Desktop Runtime x64.

If setup says .NET is missing:

1. Let the installer open the Microsoft runtime page or installer.
2. Install the .NET 10 Desktop Runtime x64.
3. Run `WyspaSetup-0.6.1-win-x64.exe` again.

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
- Click Save and test key.
- Confirm the key is active in the Groq Console.

Wyspa checks the key by calling Groq's models endpoint and looking for transcription model availability.

## Groq Model Not Available

Open Settings > Groq and select Refresh models. If a saved model is marked unavailable, choose a compatible replacement from its dropdown. A network or authentication failure keeps your previous choices. Newly introduced model families may require a Wyspa compatibility update because Groq does not document capabilities in the model-list response.

Try:

- creating a new Groq API key;
- checking your Groq account access;
- waiting and testing again if Groq has a temporary issue.

## Network Or Rate Limit Error

Check your internet connection and try Save and test key again.

Groq errors may be temporary. Rate limit errors usually clear after waiting a short time.

## Microphone Is Not Detected

Check:

- Windows Settings > Privacy & security > Microphone;
- microphone access for desktop apps;
- the selected input device in Wyspa Settings > Audio & capture;
- whether the Windows input meter moves while speaking;
- whether another app has exclusive control of the microphone.

After changing devices, refresh or restart Wyspa.

## Transcript Is Always "Thank you."

This usually means Groq received silence or the wrong microphone input.

Try:

- choose the correct microphone in Settings > Audio & capture;
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

## AutoCapture Media Handling Does Not Affect Music

Open Settings > Audio & capture and check AutoCapture Media Handling.

Options:

- Do Nothing leaves audio untouched.
- Mute System Output mutes the default Windows output device while AutoCapture listening is on, then restores the previous mute state.
- Pause or Resume Media sends the standard Windows play/pause media key when listening turns on and again when listening turns off.

If play/pause does nothing, the current media app may not respond to Windows media keys. Try the mute option instead.

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

Open Settings > System and adjust Overlay Background Opacity.

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


## Conversation notes (0.7.0)

- **Selected app is unavailable:** per-app capture requires Windows build 20348 or newer. On Windows 10 build 19045 use Output device. A browser process includes its child processes and may include multiple tabs.
- **Other side appears in your microphone messages:** use headphones or reduce speaker leakage into the microphone. Source separation is not acoustic echo cancellation.
- **One room speaker gets several labels:** lower the room speaker-matching setting slightly and try a new session. If distinct voices merge, raise it. These labels are experimental and not biometric identity verification. Select your own detected speaker after speaking.
- **Speech is missing or noise becomes text:** adjust Speech threshold. Lower values capture quieter speech; higher values reject more background noise. Shorter maximum passages reduce buffering delay but create more API requests; Groq applies a minimum billed duration to each request.
- **Stop takes too long:** use Cancel pending. Completed text stays saved and skipped passages get explicit markers.
- **Cannot save notes:** keep the app open, resolve disk space or folder access, then select Save again. The notes folder contains both JSON and TXT copies.
- **YouTube blocks a public video:** region restrictions, removed videos, and automated-download challenges can prevent extraction. Wyspa does not bypass login/challenges or use browser cookies. Use Audio Files for an audio file you already have.
- **Speaker/video tools missing:** reinstall the complete v7 package. For development run `python scripts/prepare-v7-tools.py` before publishing.
