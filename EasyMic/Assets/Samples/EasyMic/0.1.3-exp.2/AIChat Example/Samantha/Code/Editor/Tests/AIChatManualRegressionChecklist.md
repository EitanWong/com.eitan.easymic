# AIChat Manual Regression Checklist

1. First startup without `ai_chat_config.json`:
   - Delete the runtime config file under `Application.persistentDataPath`.
   - Launch scene and confirm file is recreated automatically.
   - Confirm controller initializes and no fatal errors are logged.

2. Runtime config panel save and reload:
   - Open runtime config panel.
   - Change API base URL / model / TTS / ASR fields.
   - Save and let scene reload.
   - Reopen panel and verify values persisted.

3. Remote TTS flow:
   - Disable local TTS.
   - Submit a chat message.
   - Verify streaming response and sentence-by-sentence speech playback.
   - Verify speech completion transitions back to idle.

4. Local TTS flow:
   - Enable local TTS and valid local synthesizer.
   - Submit a chat message.
   - Verify audio output starts and completes.
   - Verify speaking state mirrors local synth state.

5. User interrupt during assistant speech:
   - Trigger assistant response playback.
   - Speak while assistant is talking.
   - Verify active response is cancelled and state returns to idle.

6. Network timeout and recovery:
   - Force an unreachable API endpoint or disconnect network.
   - Submit chat request and verify failure event/message appears.
   - Restore network and verify subsequent requests recover.

7. Plugin lifecycle timing:
   - Enable proactive conversation plugin.
   - Verify lifecycle callbacks are still triggered (chat activated, request started, response finished, idle state changes).
   - Verify proactive send still respects idle and speaking constraints.
