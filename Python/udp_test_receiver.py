"""Print packets from body_tracking_sender.py (test without Unity). Port 5065."""
import json
import socket

sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
sock.bind(("127.0.0.1", 5065))
print("listening on 5065 ... Ctrl+C to stop")
while True:
    data, _ = sock.recvfrom(65535)
    pkt = json.loads(data)
    line = [f"src={pkt['src']} fps={pkt['fps']:4.1f}"]
    for name in ("P1", "P2"):
        p = pkt[name]
        if not p["active"]:
            line.append(f"{name}: {p['zone']}")
            continue
        line.append(f"{name}: bag=({p['bag_x']:.2f},{p['bag_y']:.2f}) "
                    f"grab=({p['grab_x']:.2f},{p['grab_y']:.2f}) pinch={p['pinch']} "
                    f"#{p['pinch_n']} pose={p['pose']} d={p['d']:.2f}")
    print(" | ".join(line), end="\r")
