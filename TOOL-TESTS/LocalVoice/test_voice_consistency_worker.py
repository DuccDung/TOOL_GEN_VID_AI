"""Pure worker tests; no models, network, TTS or provider credentials."""
import importlib.util
import contextlib
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

SOURCE = Path(__file__).resolve().parents[2] / "TOOL-LOCAL/LocalVoice/Workers/voice_consistency_worker.py"
spec = importlib.util.spec_from_file_location("voice_consistency_worker", SOURCE)
worker = importlib.util.module_from_spec(spec)
spec.loader.exec_module(worker)


class VoiceWorkerTests(unittest.TestCase):
    @unittest.skipUnless(importlib.util.find_spec("torch"), "Pinned CPU runtime is needed for attention regression")
    def test_cpu_attention_avoids_native_kernel_that_crashed_demucs(self):
        import torch
        from torch.utils._python_dispatch import TorchDispatchMode

        class RejectFusedAttention(TorchDispatchMode):
            def __torch_dispatch__(self, func, types, args=(), kwargs=None):
                if "scaled_dot_product_flash_attention" in str(func):
                    raise AssertionError("unsafe fused attention")
                return func(*args, **(kwargs or {}))

        worker.configure_cpu(torch)
        attention = torch.nn.MultiheadAttention(16, 2, batch_first=True).eval()
        values = torch.randn(1, 20, 16)
        with patch.object(torch, "_native_multi_head_attention", side_effect=AssertionError("unsafe native MHA")):
            with torch.inference_mode(), RejectFusedAttention():
                result, _ = attention(values, values, values, need_weights=False)
        self.assertEqual(values.shape, result.shape)
        self.assertTrue(torch.isfinite(result).all().item())

    @unittest.skipUnless(worker.os.name == "nt", "Windows process-memory diagnostic")
    def test_completed_reports_memory_of_actual_worker_not_venv_launcher(self):
        allocation = bytearray(16 * 1024 * 1024)
        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            worker.emit("completed")
        event = json.loads(output.getvalue())
        self.assertEqual("completed", event["type"])
        self.assertGreaterEqual(event["peakWorkingSetBytes"], len(allocation))

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
