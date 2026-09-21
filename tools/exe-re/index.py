"""Index an objdump disassembly: function starts and per-function summaries.

    objdump -d --no-show-raw-insn -j .text <exe> > text.asm
    python3 index.py <exe> text.asm refs  0x1431bd160 ...   # functions referencing addresses
    python3 index.py <exe> text.asm summary 0x14053bfa0 ...  # calls and strings of a function
    python3 index.py <exe> text.asm callers 0x1407c5660 ...  # direct callers and pointer slots
    python3 index.py <exe> text.asm dump    0x1407b9cd0 ...  # a function's body, strings annotated

Function starts are taken as the first instruction after int3 padding. That is a
heuristic: it misses functions that follow one another without padding and can split a
function at an embedded int3. Check a start by reading the prologue before relying on it.
"""
import bisect, re, sys
from pe import PE

exe, asm, mode, *targets = sys.argv[1:]
p = PE(exe)
line = re.compile(r'^\s+([0-9a-f]+):\t(.*)$')
parsed = [(int(m.group(1), 16), m.group(2)) for m in map(line.match, open(asm)) if m]
addrs = [a for a, _ in parsed]
starts = [parsed[0][0]] + [parsed[i][0] for i in range(1, len(parsed))
                           if parsed[i - 1][1].startswith('int3') and not parsed[i][1].startswith('int3')]


def function_of(a):
    i = bisect.bisect_right(starts, a) - 1
    return starts[i], starts[i + 1] if i + 1 < len(starts) else a + 1


def body(a):
    fs, fe = function_of(a)
    i = bisect.bisect_left(addrs, fs)
    while i < len(parsed) and parsed[i][0] < fe:
        yield parsed[i]
        i += 1


for t in targets:
    target = int(t, 16)
    if mode == 'refs':
        hits = sorted({function_of(a)[0] for a, ins in parsed if re.search(r'\b0x%x\b' % target, ins)})
        print(f'{target:#x}: referenced from {len(hits)} functions:', ' '.join(hex(h) for h in hits))
    elif mode == 'summary':
        fs, fe = function_of(target)
        calls, strings = set(), []
        for _, ins in body(target):
            if m := re.search(r'call\s+0x([0-9a-f]+)', ins):
                calls.add(m.group(1))
            if m := re.search(r'# 0x([0-9a-f]+)', ins):
                try:
                    s = p.cstr(int(m.group(1), 16))
                    if 3 <= len(s) < 80 and all(32 <= ord(c) < 127 for c in s):
                        strings.append(s)
                except Exception:
                    pass
        print(f'{fs:#x}-{fe:#x} ({fe - fs} bytes) calls: {" ".join(sorted(calls))}')
        print('    strings:', strings)
    elif mode == 'callers':
        direct = sorted({function_of(a)[0] for a, ins in parsed if re.search(r'(call|jmp)\s+0x%x\b' % target, ins)})
        print(f'{target:#x}: called by', ' '.join(hex(c) for c in direct) or 'nothing directly',
              '| stored at', ' '.join(hex(v) for v in p.pointers_to(target)) or 'nowhere')
    elif mode == 'dump':
        fs, fe = function_of(target)
        print(f'; {fs:#x}-{fe:#x} ({fe - fs} bytes)')
        for a, ins in body(target):
            note = ''
            if m := re.search(r'# 0x([0-9a-f]+)', ins):
                try:
                    s = p.cstr(int(m.group(1), 16))
                    if 3 <= len(s) < 80 and all(32 <= ord(c) < 127 for c in s):
                        note = f'\t; "{s}"'
                except Exception:
                    pass
            print(f'{a:x}:\t{ins}{note}')
