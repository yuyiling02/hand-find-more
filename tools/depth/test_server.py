import socket
import threading
import unittest

import numpy as np
from server import palm_sample, receive


class ProtocolTests(unittest.TestCase):
    def test_fragmented_read(self):
        left, right = socket.socketpair()
        left.settimeout(2)
        right.settimeout(2)
        with left, right:
            def write():
                left.sendall(b'ab')
                left.sendall(b'cd')
            worker = threading.Thread(target=write)
            worker.start()
            self.assertEqual(receive(right, 4), b'abcd')
            worker.join()

    def test_disconnect(self):
        left, right = socket.socketpair()
        right.settimeout(2)
        left.close()
        with right, self.assertRaises(EOFError):
            receive(right, 4)

    def test_palm_excludes_background(self):
        depth = np.zeros((100, 100), dtype=np.float32)
        depth[30:70, 30:70] = 5
        landmarks = [{'x': 0.5, 'y': 0.5} for _ in range(21)]
        for index, point in zip((0, 5, 9, 13, 17), ((.5,.7),(.3,.5),(.4,.3),(.6,.3),(.7,.5))):
            landmarks[index] = dict(zip(('x', 'y'), point))
        median, mad = palm_sample(depth, landmarks)
        self.assertEqual(median, 5)
        self.assertEqual(mad, 0)

    def test_degenerate_palm(self):
        with self.assertRaises(ValueError):
            palm_sample(np.ones((100, 100)), [{'x': .5, 'y': .5}] * 21)


if __name__ == '__main__':
    unittest.main()
