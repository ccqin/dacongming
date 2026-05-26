import os

key = """-----BEGIN OPENSSH PRIVATE KEY-----
b3BlbnNzaC1rZXktdjEAAAAABG5vbmUAAAAEbm9uZQAAAAAAAAABAAAAMwAAAAtzc2gtZW
QyNTUxOQAAACDK8aQvhC+J16mvZEdsBPLhGrotB0bZBEDN2nKDAFFh+gAAAJgJY1OnCWNT
pwAAAAtzc2gtZWQyNTUxOQAAACDK8aQvhC+J16mvZEdsBPLhGrotB0bZBEDN2nKDAFFh+g
AAAEBYXI9mpmpeFKKnKAzZQ2j3iBr+ZtwrVpm7e8VDnKiy98rxpC+EL4nXqa9kR2wE8uEa
ui0HRtkEQM3acoMAUWH6AAAAFGFnZW50QGJvdC1mb3ItbXlyZXBvAQ==
-----END OPENSSH PRIVATE KEY-----
"""

path = os.path.expanduser("~/.ssh/id_gitee")
with open(path, "w", newline="\n", encoding="utf-8") as f:
    f.write(key)
os.system(f'icacls "{path}" /inheritance:r /grant:r Administrator:\(R\)')
print("Done. Testing...")
os.system("ssh -T git@gitee.com")
