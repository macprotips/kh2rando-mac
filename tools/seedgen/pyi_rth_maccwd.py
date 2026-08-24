import os
base = os.path.join(os.path.expanduser("~"), "Library", "Application Support", "KH2SeedGenerator")
os.makedirs(base, exist_ok=True)
os.chdir(base)
