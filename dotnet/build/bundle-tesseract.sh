#!/usr/bin/env bash
# bundle-tesseract.sh <publish dir>: puts Tesseract, each library it needs that a GNOME install
# may lack, and the languages Tinysnap reads into the published app, so the AppImage reads text
# with nothing installed. Runs on Ubuntu 24.04 with libtesseract5 and patchelf installed.
set -euo pipefail
out=$1
mkdir -p "$out/tesseract" "$out/tessdata"

lib=$(ldconfig -p | awk '/libtesseract\.so\.5 / { print $NF; exit }')
[ -n "$lib" ] || { echo "libtesseract.so.5 is not installed" >&2; exit 1; }
cp -L "$lib" "$out/tesseract/libtesseract.so.5"
# glibc and the C++ runtime stay the system's: a second copy of either in one process breaks it.
skip='^(linux-vdso|linux-gate|ld-linux[^ ]*|libc|libm|libdl|libpthread|librt|libresolv|libstdc\+\+|libgcc_s)\.so'
ldd "$lib" | awk '/=> \// { print $1, $3 }' | while read -r name path; do
  [[ $name =~ $skip ]] && continue
  cp -L "$path" "$out/tesseract/$name"
done
# Each finds the others beside it, wherever the AppImage is mounted.
for file in "$out"/tesseract/*.so*; do patchelf --set-rpath '$ORIGIN' "$file"; done
# Every library's licence travels with it, as its package has it.
mkdir -p "$out/tesseract/licenses"
{ echo "$lib"; ldd "$lib" | awk '/=> \// { print $1, $3 }' | while read -r name path; do [[ $name =~ $skip ]] || echo "$path"; done; } |
  while read -r path; do
    package=$(dpkg -S "$(readlink -f "$path")" 2>/dev/null | head -1 | cut -d: -f1) || true
    [ -n "$package" ] && [ -f "/usr/share/doc/$package/copyright" ] && cp "/usr/share/doc/$package/copyright" "$out/tesseract/licenses/$package.txt"
  done

# The languages the Mac app reads, from the fast models, at a fixed tag.
for language in eng fra ita deu spa por chi_sim chi_tra kor jpn rus ukr tha vie ara; do
  curl -fsSL --retry 3 -o "$out/tessdata/$language.traineddata" \
    "https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/4.1.0/$language.traineddata"
done
ls -l "$out/tesseract"
du -sh "$out/tesseract" "$out/tessdata"
