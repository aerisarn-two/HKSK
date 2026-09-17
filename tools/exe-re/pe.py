"""Minimal PE reader for an x64 executable: address translation, C strings, qwords,
and RTTI virtual tables. No dependencies -- this machine has neither pefile nor numpy.
"""
import struct


class PE:
    def __init__(self, path):
        d = self.d = open(path, 'rb').read()
        pe = struct.unpack_from('<I', d, 0x3c)[0]
        count = struct.unpack_from('<H', d, pe + 6)[0]
        optional_size = struct.unpack_from('<H', d, pe + 20)[0]
        optional = pe + 24
        self.image_base = struct.unpack_from('<Q', d, optional + 24)[0]
        self.entry_rva = struct.unpack_from('<I', d, optional + 16)[0]
        self.sections = []
        for i in range(count):
            o = optional + optional_size + i * 40
            name = d[o:o + 8].rstrip(b'\0').decode()
            vsize, va, rawsize, rawptr = struct.unpack_from('<IIII', d, o + 8)
            self.sections.append((name, va, vsize, rawptr, rawsize))

    def off2va(self, off):
        for _, va, _, rawptr, rawsize in self.sections:
            if rawptr <= off < rawptr + rawsize:
                return self.image_base + va + off - rawptr
        return None

    def va2off(self, va_abs):
        rva = va_abs - self.image_base
        for _, va, vsize, rawptr, rawsize in self.sections:
            if va <= rva < va + max(vsize, rawsize):
                return rawptr + rva - va
        return None

    def cstr(self, va_abs):
        o = self.va2off(va_abs)
        return self.d[o:self.d.index(b'\0', o)].decode('latin1')

    def qword(self, va_abs):
        return struct.unpack_from('<Q', self.d, self.va2off(va_abs))[0]

    def string_va(self, text):
        """Every VA at which a NUL-terminated copy of text starts."""
        needle, out, i = text.encode() + b'\0', [], 0
        while (i := self.d.find(needle, i)) >= 0:
            out.append(self.off2va(i))
            i += 1
        return out

    def pointers_to(self, va_abs):
        """Every VA holding an absolute pointer to va_abs (vtable slots, setting records)."""
        needle, out, i = struct.pack('<Q', va_abs), [], 0
        while (i := self.d.find(needle, i)) >= 0:
            out.append(self.off2va(i))
            i += 1
        return out

    def rtti_vftables(self, class_name):
        """The virtual tables of a class, found through MSVC x64 RTTI.

        TypeDescriptor = the ".?AV<name>@@" string minus 16 bytes. A
        CompleteObjectLocator has signature 1, the descriptor's RVA at +12 and its own
        RVA at +20; the vftable starts 8 bytes after the qword that points at it.
        """
        so = self.d.find(('.?AV%s@@' % class_name).encode())
        if so < 0:
            return []
        td_rva = self.off2va(so - 16) - self.image_base
        out, i = [], 0
        needle = struct.pack('<I', td_rva)
        while (i := self.d.find(needle, i)) >= 0:
            col_off = i - 12
            if (struct.unpack_from('<I', self.d, col_off)[0] == 1 and
                    struct.unpack_from('<I', self.d, col_off + 20)[0] == self.off2va(col_off) - self.image_base):
                out += [va + 8 for va in self.pointers_to(self.off2va(col_off))]
            i += 1
        return out

    def setting(self, name_with_section):
        """A Bethesda INI setting record: +0 vtable, +8 value, +16 name pointer."""
        out = []
        for s in self.string_va(name_with_section):
            for holder in self.pointers_to(s):
                record = holder - 16
                out.append((record, self.qword(record), self.d[self.va2off(record + 8)]))
        return out
