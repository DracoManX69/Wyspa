"""Wyspa's isolated local inference worker. Pinned runtime; no network/model downloads.
Redux ONNX tensor conventions follow eschmidbauer/parakeet-redux-onnx (CC BY 4.0),
converted from Moondream/NVIDIA Parakeet Redux. Wyspa implements the decoder here.
"""
import sys, os, json, time, pathlib, gc
os.environ['HF_HUB_OFFLINE'] = '1'
os.environ['HF_HUB_DISABLE_TELEMETRY'] = '1'
os.environ['HF_HUB_DISABLE_XET'] = '1'
_dll_handles = []
if hasattr(os, 'add_dll_directory'):
    for p in os.environ.get('WYSPA_CUDA_PATHS', '').split(os.pathsep):
        if p and pathlib.Path(p).is_dir():
            _dll_handles.append(os.add_dll_directory(p))
import numpy as np

class Redux:
    def __init__(self, directory, threads):
        import onnxruntime as ort
        directory = pathlib.Path(directory)
        self.config = json.loads((directory/'config.json').read_text(encoding='utf-8'))
        self.tokens = {}
        for line in (directory/'vocab.txt').read_text(encoding='utf-8').splitlines():
            if line:
                token, index = line.rsplit(' ', 1); self.tokens[int(index)] = token
        options = ort.SessionOptions(); options.intra_op_num_threads = threads
        options.inter_op_num_threads = 1
        options.add_session_config_entry('session.intra_op.allow_spinning', '0')
        def session(name): return ort.InferenceSession(str(directory/name), options, providers=['CPUExecutionProvider'])
        self.pre = session('preprocessor.onnx'); self.encoder = session('encoder-model.onnx')
        self.decoder = session('decoder_joint-model.onnx')
        shape = next(i.shape for i in self.decoder.get_inputs() if i.name == 'input_states_1')
        self.state_shape = (int(shape[0]), 1, int(shape[2]))

    def transcribe(self, pcm):
        # Bounded clips keep encoder memory independent of long audio duration.
        segments = []
        for start in range(0, pcm.size, 30*16000):
            chunk = pcm[start:start+30*16000]
            if chunk.size < 320: continue
            features, lengths = self.pre.run(None, {'waveforms': chunk[None], 'waveforms_lens': np.array([chunk.size], np.int64)})
            outputs, valid = self.encoder.run(None, {'audio_signal': features, 'length': lengths})
            frames = np.ascontiguousarray(outputs[0, :, :int(valid[0])].T)
            hidden = np.zeros(self.state_shape, np.float32); cell = hidden.copy()
            blank = int(self.config['blank_token_id']); previous = blank; frame = 0; pieces = []
            budget = int(self.config['max_tokens_per_step'])*len(frames)
            vocab_size = int(self.config['vocab_size']); durations = self.config['durations']
            while frame < len(frames) and budget > 0:
                logits, _, h, c = self.decoder.run(None, {'encoder_outputs': frames[frame][None,:,None],
                    'targets': np.array([[previous]], np.int32), 'target_length': np.ones(1,np.int32),
                    'input_states_1': hidden, 'input_states_2': cell})
                values = logits[0,0,0]; token = int(values[:vocab_size].argmax())
                advance = int(durations[int(values[vocab_size:].argmax())])
                if token == blank and advance == 0: advance = 1
                if token != blank:
                    previous = token; hidden, cell = h, c
                    piece = self.tokens[token]
                    if piece not in ('<blk>','<unk>','<pad>'): pieces.append(piece)
                frame += advance; budget -= 1
            text = ''.join(pieces).replace('▁',' ').strip()
            if text: segments.append({'start': start/16000., 'end': (start+chunk.size)/16000., 'text': text})
        return ' '.join(s['text'] for s in segments), segments

model = None; model_key = None
for line in sys.stdin:
    try:
        request = json.loads(line); op = request['op']
        if op == 'prepare':
            key = (request['engine'], request['directory'], request['gpu'], request['threads'])
            if key != model_key:
                model = None; gc.collect()
                if request['engine'] == 'FasterWhisper':
                    from faster_whisper import WhisperModel
                    model = WhisperModel(request['directory'], device='cuda' if request['gpu'] else 'cpu',
                        compute_type='int8_float16' if request['gpu'] else 'int8', cpu_threads=request['threads'],
                        num_workers=1, local_files_only=True)
                elif request['engine'] == 'Redux':
                    model = Redux(request['directory'], request['threads'])
                else: raise ValueError('Unsupported managed engine')
                model_key = key
            result = {'device': 'GPU' if request['gpu'] else 'CPU'}
        elif op == 'transcribe':
            pcm = np.fromfile(request['pcm'], dtype='<f4')
            if model_key[0] == 'FasterWhisper':
                language = request.get('language') or None
                segments, _ = model.transcribe(pcm, language=language, beam_size=1, best_of=1,
                    temperature=0, condition_on_previous_text=False, vad_filter=False,
                    initial_prompt=request.get('prompt') or None, word_timestamps=False)
                rows = [{'start': s.start, 'end': s.end, 'text': s.text.strip()} for s in segments if s.text.strip()]
                result = {'text': ' '.join(s['text'] for s in rows), 'segments': rows}
            else:
                text, rows = model.transcribe(pcm); result = {'text': text, 'segments': rows}
        else: raise ValueError('Unsupported worker operation')
        result['cpu'] = time.process_time()
        print(json.dumps(result, ensure_ascii=False), flush=True)
    except Exception as e:
        # No source audio/transcript is written to logs; caller receives an explicit error.
        print(json.dumps({'error': str(e)}), flush=True)
