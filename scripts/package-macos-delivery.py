#!/usr/bin/env python3
"""Assemble the customer handoff: two DMGs and a single offline HTML guide."""
from pathlib import Path
from html.parser import HTMLParser
import hashlib, shutil, zipfile, argparse
parser=argparse.ArgumentParser();parser.add_argument('--version',default='2.1.0');args=parser.parse_args()
root=Path(__file__).resolve().parents[1]
output=root/'交付产品'/f'有序连点器-macOS-{args.version}'
installers=output/'安装包';installers.mkdir(parents=True,exist_ok=True)
for arch in ['arm64','x64']:
 name=f'ordered-clicker-macos-{arch}-v{args.version}.dmg'
 source=root/'releases/macos'/name
 if not source.is_file():raise SystemExit(f'Missing release package: {source}')
 shutil.copy2(source,installers/name)
shutil.copy2(root/'docs/macos/产品说明.html',output/'产品说明.html')
class Links(HTMLParser):
 def handle_starttag(self,tag,attrs):
  attrs=dict(attrs)
  if tag=='a' and attrs.get('href','').startswith('安装包/'):
   target=output/attrs['href']
   if not target.is_file():raise AssertionError(f'Broken installer link: {target}')
Links().feed((output/'产品说明.html').read_text())
def sha(path):
 h=hashlib.sha256()
 with path.open('rb') as f:
  for data in iter(lambda:f.read(1024*1024),b''):h.update(data)
 return h.hexdigest()
files=sorted(p for p in output.rglob('*') if p.is_file() and p.name!='SHA256SUMS.txt')
(output/'SHA256SUMS.txt').write_text('\n'.join(f'{sha(p)}  {p.relative_to(output).as_posix()}' for p in files)+'\n')
archive=output.parent/(output.name+'-交付.zip')
with zipfile.ZipFile(archive,'w') as z:
 for path in sorted(output.rglob('*')):
  if path.is_file():z.write(path,path.relative_to(output.parent),compress_type=zipfile.ZIP_STORED if path.suffix=='.dmg' else zipfile.ZIP_DEFLATED)
with zipfile.ZipFile(archive) as z:
 error=z.testzip()
 if error:raise AssertionError(error)
print(archive)
print(output/'产品说明.html')
