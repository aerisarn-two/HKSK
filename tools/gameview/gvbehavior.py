"""Write a behaviour packfile the 6.6 runtime will load.

Nodes are described in Python and every member is emitted from the runtime's
own class definition, so a node carries exactly the fields this build expects,
in its order, with its enum values -- none of which is guessed.

    graph = Behaviour(
        variables={'Left': 0.0, 'Right': 0.0},
        root=Blend('Root', [
            (1.0, Clip('Left',  ANIM_A, bind={'userControlledTimeFraction': 'Left'})),
            (1.0, Clip('Right', ANIM_B, bind={'userControlledTimeFraction': 'Right'})),
        ]))
    open(path, 'w').write(graph.xml(exe))
"""
import os
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import hkreflect as R
import hkpack

SERIALIZE_IGNORED = 1024
UNTYPED = {'hkbClipTriggerArray', 'hkbBoneWeightArray', 'hkbEventInfo',
           'hkbGenerator', 'hkbNode'}


def enum_names(img, cs, byname, name):
    """Every value the class chain's declared enums can take.  Enums have to go
    into the file by name: this build leaves the per-member enum pointer null,
    and a bare number is read as zero without complaint -- which is how
    VARIABLE_TYPE_REAL became VARIABLE_TYPE_BOOL and silently stopped every
    binding on the graph."""
    out = {}
    for k in chain(cs, byname, name):
        for _, items in R.enums(img, k).items():
            for item, value in items.items():
                out.setdefault(value, []).append(item)
    return {v: names[0] for v, names in out.items() if len(names) == 1}


def chain(cs, byname, name):
    """A class and its ancestors, base first -- the order a packfile wants."""
    out, k = [], byname[name]
    while k:
        out.append(k)
        k = cs.get(k['parent'])
        if k and k['name'] not in byname:
            k = None
    return list(reversed(out))


class Node:
    """One object in the file: a class name plus the members that differ from
    their defaults, and optionally a binding of members to variables."""

    def __init__(self, cls, name=None, bind=None, **values):
        self.cls, self.values = cls, dict(values)
        self.bind = dict(bind or {})
        if name is not None:
            self.values['name'] = name
        self.id = None

    def children(self):
        return []


class Clip(Node):
    def __init__(self, name, animation, mode='MODE_USER_CONTROLLED', binding=-1, **kw):
        super().__init__('hkbClipGenerator', name, animationName=animation,
                         mode=mode, playbackSpeed=1.0, animationBindingIndex=binding, **kw)


class Blend(Node):
    def __init__(self, name, arms, **kw):
        super().__init__('hkbBlenderGenerator', name, **kw)
        self.arms = [Node('hkbBlenderGeneratorChild', weight=w, worldFromModelWeight=1.0,
                          generator=g) for w, g in arms]
        self.values['children'] = self.arms

    def children(self):
        return self.arms + [a.values['generator'] for a in self.arms]


class Behaviour:
    def __init__(self, root, variables=None, events=()):
        self.root, self.events = root, list(events)
        self.variables = dict(variables or {})

    def _objects(self):
        seen, order = set(), []

        def visit(n):
            if id(n) in seen:
                return
            seen.add(id(n))
            order.append(n)
            for c in n.children():
                visit(c)
        visit(self.root)
        return order

    def xml(self, exe):
        img, cs, byname = hkpack.load(exe)
        nodes = self._objects()

        names = list(self.variables)
        values = [struct.unpack('<i', struct.pack('<f', float(self.variables[n])))[0]
                  for n in names]

        graph = Node('hkbBehaviorGraph', 'Graph', variableMode=0)
        def bits(x):
            return struct.unpack('<i', struct.pack('<f', float(x)))[0]

        data = Node('hkbBehaviorGraphData',
                    variableInfos=[{'role': {'role': 0, 'flags': 0}, 'type': 'VARIABLE_TYPE_REAL'} for _ in names],
                    wordMinVariableValues=[{'value': bits(0.0)} for _ in names],
                    wordMaxVariableValues=[{'value': bits(1.0)} for _ in names])
        initial = Node('hkbVariableValueSet',
                       wordVariableValues=[{'value': v} for v in values])
        strings = Node('hkbBehaviorGraphStringData',
                       variableNames=names, eventNames=list(self.events))

        objects = [graph, data, initial, strings] + nodes
        for n in nodes:
            if n.bind:
                bindings = [{'memberPath': path, 'variableIndex': names.index(var),
                             'bitIndex': -1, 'bindingType': 0}
                            for path, var in n.bind.items()]
                n.binding_set = Node('hkbVariableBindingSet', bindings=bindings,
                                     indexOfBindingToEnable=-1)
                objects.append(n.binding_set)
                n.values['variableBindingSet'] = n.binding_set

        for i, o in enumerate(objects):
            o.id = f'#{200 + i:04d}'
        graph.values.update(rootGenerator=self.root, data=data)
        data.values.update(variableInitialValues=initial, stringData=strings)

        declare = []
        for o in objects:
            for k in chain(cs, byname, o.cls):
                if k['name'] not in declare:
                    declare.append(k['name'])
        for extra in ('hkbVariableInfo', 'hkbRoleAttribute', 'hkbVariableValue',
                      'hkbVariableBindingSetBinding', 'hkRootLevelContainerNamedVariant',
                      'hkRootLevelContainer'):
            if extra not in declare:
                declare.append(extra)

        types, ids = hkpack.types_section(img, cs, byname, declare, UNTYPED)
        body = [hkpack.root('hkbBehaviorGraph', graph.id, ids['hkbBehaviorGraph'])]
        for o in objects:
            body.append(self._object(img, cs, byname, o))
        return hkpack.packfile(types, '\n'.join(body))

    def _object(self, img, cs, byname, obj):
        rows, fallback = [], {}
        for k in chain(cs, byname, obj.cls):
            own = R.members(img, k, cs)
            fallback.update(R.defaults(img, k, own))
            rows += own
        enums = enum_names(img, cs, byname, obj.cls)
        lines = [f'\t\t<hkobject name="{obj.id}" class="{obj.cls}">']
        for r in rows:
            value = obj.values.get(r['name'], fallback.get(r['name']))
            lines.append(self._member(img, cs, byname, r, value, enums=enums))
        lines.append('\t\t</hkobject>\n')
        return '\n'.join(lines)

    def _member(self, img, cs, byname, r, value, indent='\t\t\t', enums=None):
        name = r['name']
        if r['flags'] & SERIALIZE_IGNORED:
            return f'{indent}<hkparam name="{name}"><!-- zero {name} --></hkparam>'
        body = self._value(img, cs, byname, r, value, indent, enums or {})
        return f'{indent}<hkparam name="{name}"{body}'

    def _value(self, img, cs, byname, r, value, indent, enums):
        t, sub = r['type'], r['subtype']
        if t == 'TYPE_ARRAY':
            items = value or []
            if not items:
                return ' numelements="0"></hkparam>'
            if sub == 'TYPE_CSTRING':
                body = '\n'.join(f'{indent}\t<hkcstring>{v}</hkcstring>' for v in items)
            elif sub == 'TYPE_POINTER':
                body = indent + '\t' + ' '.join(v.id for v in items)
            elif sub == 'TYPE_STRUCT':
                body = '\n'.join(self._struct(img, cs, byname, r['cls'], v, indent + '\t')
                                 for v in items)
            else:
                body = indent + '\t' + ' '.join(str(v) for v in items)
            return f' numelements="{len(items)}">\n{body}\n{indent}</hkparam>'
        if t == 'TYPE_STRUCT':
            return '>\n' + self._struct(img, cs, byname, r['cls'], value or {}, indent + '\t') \
                   + f'\n{indent}</hkparam>'
        return f'>{self._scalar(r, value, enums)}</hkparam>'

    def _struct(self, img, cs, byname, cls, values, indent):
        rows, fallback = [], {}
        for k in chain(cs, byname, cls):
            own = R.members(img, k, cs)
            fallback.update(R.defaults(img, k, own))
            rows += own
        enums = enum_names(img, cs, byname, cls)
        lines = [f'{indent}<hkobject>']
        for r in rows:
            lines.append(self._member(img, cs, byname, r,
                                      values.get(r['name'], fallback.get(r['name'])),
                                      indent + '\t', enums))
        lines.append(f'{indent}</hkobject>')
        return '\n'.join(lines)

    @staticmethod
    def _scalar(r, value, enums):
        t = r['type']
        if t in ('TYPE_ENUM', 'TYPE_FLAGS') and not isinstance(value, str):
            value = value or 0
            if value == 0:
                return '0'
            if value in enums:
                return enums[value]
            raise ValueError(f'{r["name"]}: enum value {value} has no unique name; '
                             f'pass the name, a number is read as zero')
        if t == 'TYPE_REAL':
            return f'{float(value or 0):.6f}'
        if t == 'TYPE_BOOL':
            return 'true' if value else 'false'
        if t == 'TYPE_CSTRING':
            return value if value else '&#0;'
        if t == 'TYPE_POINTER':
            return value.id if isinstance(value, Node) else 'null'
        if t == 'TYPE_VARIANT':
            return 'null'
        if t in ('TYPE_VECTOR4', 'TYPE_QUATERNION'):
            return value or '(0.000000 0.000000 0.000000 0.000000)'
        return str(value if value is not None else 0)
