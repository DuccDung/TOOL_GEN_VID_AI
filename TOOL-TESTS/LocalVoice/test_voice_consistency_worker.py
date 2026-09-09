"""Pure worker tests; no models, network, TTS or provider credentials."""
import importlib.util
from pathlib import Path
import tempfile
import unittest

SOURCE = Path(__file__).resolve().parents[2] / "TOOL-LOCAL/LocalVoice/Workers/voice_consistency_worker.py"
spec = importlib.util.spec_from_file_location("voice_consistency_worker", SOURCE)
worker = importlib.util.module_from_spec(spec)
spec.loader.exec_module(worker)


class VoiceWorkerTests(unittest.TestCase):
    def test_fit_length_only_pads_small_tail_never_time_stretches(self):
        import numpy as np
        value = np.arange(100, dtype=np.float32)
        result = worker.fit_length(value, 105, 1000)
        np.testing.assert_array_equal(value, result[:100])
        np.testing.assert_array_equal(np.zeros(5), result[100:])
        with self.assertRaisesRegex(ValueError, "timing_drift_exceeded"):
            worker.fit_length(value, 250, 1000)

    def test_speech_mask_preserves_native_outside_windows(self):
        import numpy as np
        result = worker.speech_mask(1000, [[200, 700]], 1000)
        self.assertTrue(np.all(result[:200] == 0))
        self.assertTrue(np.all(result[700:] == 0))
        self.assertTrue(np.all(result[220:680] == 1))
        self.assertEqual(0, result[200])
        self.assertEqual(0, result[699])

    def test_invalid_window_and_path_are_rejected(self):
        with self.assertRaisesRegex(ValueError, "invalid_speech_window"):
            worker.speech_mask(100, [[90, 110]], 1000)
        with tempfile.TemporaryDirectory(prefix="vm-voice-python-") as directory:
            with self.assertRaisesRegex(ValueError, "invalid_worker_path"):
                worker.resolve_beneath(Path(directory), "../escape.wav")
            with self.assertRaisesRegex(ValueError, "invalid_worker_path"):
                worker.resolve_beneath(Path(directory), str(Path(directory) / "absolute.wav"))

    def test_checkpoint_needs_exact_source_model_and_file_bytes(self):
        with tempfile.TemporaryDirectory(prefix="vm-voice-python-") as directory:
            root = Path(directory)
            (root / "speech.wav").write_bytes(b"speech")
            worker.save_checkpoint(root, "prepared.json", "source+model", ["speech.wav"])
            self.assertTrue(worker.cache_valid(root, "prepared.json", "source+model", ["speech.wav"]))
            self.assertFalse(worker.cache_valid(root, "prepared.json", "new-source", ["speech.wav"]))
            (root / "speech.wav").write_bytes(b"changed")
            self.assertFalse(worker.cache_valid(root, "prepared.json", "source+model", ["speech.wav"]))
            self.assertFalse((root / "prepared.json.part").exists())

    def test_network_guard_and_protocol_fail_closed(self):
        with self.assertRaisesRegex(RuntimeError, "network_disabled"):
            worker.deny_network("example.invalid")
        with self.assertRaisesRegex(ValueError, "protocol_mismatch"):
            worker.run(dict(protocolVersion=2))


if __name__ == "__main__":
    unittest.main()
