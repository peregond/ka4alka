"""Signs the Android TV APK and writes its update manifest. Used only by .github/workflows/android.yml.

The APK is signed with the project's update key (a fixed, reproducible certificate is built from it), so the signing
identity is the same on every build without a separate keystore secret. The manifest is signed by openssl afterwards.
"""
import argparse, datetime, hashlib, json, os, pathlib, subprocess, sys

def certificate(key_pem: bytes, out_cert: pathlib.Path, out_key: pathlib.Path):
    from cryptography import x509
    from cryptography.hazmat.primitives import hashes, serialization
    from cryptography.x509.oid import NameOID
    key = serialization.load_pem_private_key(key_pem, password=None)
    name = x509.Name([x509.NameAttribute(NameOID.COMMON_NAME, "Kachalka"), x509.NameAttribute(NameOID.ORGANIZATION_NAME, "ka4alka")])
    cert = (x509.CertificateBuilder().subject_name(name).issuer_name(name).public_key(key.public_key()).serial_number(1)
            .not_valid_before(datetime.datetime(2024, 1, 1)).not_valid_after(datetime.datetime(2124, 1, 1))
            .sign(key, hashes.SHA256()))
    out_cert.write_bytes(cert.public_bytes(serialization.Encoding.PEM))
    out_key.write_bytes(key.private_bytes(serialization.Encoding.DER, serialization.PrivateFormat.PKCS8, serialization.NoEncryption()))

def manifest(apk: pathlib.Path, version: str, notes: str, out: pathlib.Path):
    major, minor, patch = (int(x) for x in version.split("."))
    name = f"Kachalka-AndroidTV-{version}.apk"
    assert apk.name == name, apk.name
    data = {
        "schemaVersion": 1, "appId": "kachalka", "channel": "stable", "platform": "android-tv", "version": version,
        "versionCode": major * 1_000_000 + minor * 1_000 + patch, "minSdk": 23,
        "apk": {"url": f"https://github.com/peregond/ka4alka/releases/download/android-tv/{name}", "fileName": name,
                "size": apk.stat().st_size, "sha256": hashlib.sha256(apk.read_bytes()).hexdigest()},
        "releaseNotesUrl": "https://github.com/peregond/ka4alka/releases/tag/android-tv", "notes": notes.strip(),
    }
    out.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

if __name__ == "__main__":
    p = argparse.ArgumentParser()
    sub = p.add_subparsers(dest="command", required=True)
    c = sub.add_parser("cert"); c.add_argument("key"); c.add_argument("cert"); c.add_argument("pk8")
    m = sub.add_parser("manifest"); m.add_argument("apk"); m.add_argument("version"); m.add_argument("notes"); m.add_argument("out")
    a = p.parse_args()
    if a.command == "cert":
        certificate(pathlib.Path(a.key).read_bytes(), pathlib.Path(a.cert), pathlib.Path(a.pk8))
    else:
        manifest(pathlib.Path(a.apk), a.version, a.notes, pathlib.Path(a.out))
