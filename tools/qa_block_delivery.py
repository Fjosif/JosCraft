"""Inspect incoming block GLBs independently; does not certify in-game physics."""
import hashlib,json,struct,math
from pathlib import Path
import numpy as np
ROOT=Path(__file__).resolve().parents[1]
BASE=ROOT/'collaboration/incoming/antigravity-blocks-v1'

def inspect(path):
    raw=path.read_bytes();magic,version,length=struct.unpack_from('<III',raw)
    assert (magic,version,length)==(0x46546c67,2,len(raw))
    p=12;doc=None;binary=b''
    while p<len(raw):
        n,k=struct.unpack_from('<II',raw,p);p+=8
        chunk=raw[p:p+n];p+=n
        if k==0x4e4f534a:doc=json.loads(chunk)
        elif k==0x004e4942:binary=chunk
    def access(i):
        a=doc['accessors'][i];v=doc['bufferViews'][a['bufferView']]
        count={'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4}[a['type']]
        kind={5121:'B',5123:'H',5125:'I',5126:'f'}[a['componentType']]
        fmt='<'+kind*count;size=struct.calcsize(fmt)
        offset=v.get('byteOffset',0)+a.get('byteOffset',0)
        return np.array([struct.unpack_from(fmt,binary,offset+j*v.get('byteStride',size)) for j in range(a['count'])])
    def matrix(t,q,s):
        x,y,z,w=q
        r=np.array([[1-2*y*y-2*z*z,2*x*y-2*z*w,2*x*z+2*y*w],[2*x*y+2*z*w,1-2*x*x-2*z*z,2*y*z-2*x*w],[2*x*z-2*y*w,2*y*z+2*x*w,1-2*x*x-2*y*y]])
        m=np.eye(4);m[:3,:3]=r@np.diag(s);m[:3,3]=t;return m
    def vertices(animation=None,end=True):
        props={i:{'translation':n.get('translation',[0,0,0]),'rotation':n.get('rotation',[0,0,0,1]),'scale':n.get('scale',[1,1,1])} for i,n in enumerate(doc['nodes'])}
        if animation:
            for c in animation['channels']:
                sampler=animation['samplers'][c['sampler']]
                props[c['target']['node']][c['target']['path']]=access(sampler['output'])[-1 if end else 0]
        result=[]
        def walk(i,parent):
            n=doc['nodes'][i];v=props[i]
            local=np.array(n['matrix']).reshape(4,4,order='F') if 'matrix' in n else matrix(v['translation'],v['rotation'],v['scale'])
            world=parent@local
            if 'mesh' in n:
                for prim in doc['meshes'][n['mesh']]['primitives']:
                    xyz=access(prim['attributes']['POSITION'])
                    result.extend((np.c_[xyz,np.ones(len(xyz))]@world.T)[:,:3])
            for child in n.get('children',[]):walk(child,world)
        for i in doc['scenes'][doc.get('scene',0)]['nodes']:walk(i,np.eye(4))
        return np.array(result)
    tris=sum(doc['accessors'][p['indices']]['count']//3 if 'indices' in p else doc['accessors'][p['attributes']['POSITION']]['count']//3 for m in doc['meshes'] for p in m['primitives'])
    assert tris<2000 and all('uri' not in x for x in doc.get('images',[]))
    assert all('uri' not in x for x in doc.get('buffers',[]))
    states={'rest':vertices()}
    for a in doc.get('animations',[]):states[a['name']]=vertices(a)
    for v in states.values():assert np.isfinite(v).all()
    return doc,tris,states

def main():
    meta=json.loads((BASE/'block-models.json').read_text())
    collisions=json.loads((BASE/'collision-boxes.json').read_text())['blocks']
    report={'scope':'Independent GLB binary, mesh transforms, animation endpoints, hashes, and metadata audit. No Godot physics certification.','models':[], 'issues':[]}
    for name,m in meta['blocks'].items():
        path=BASE/m['glb'];sha=hashlib.sha256(path.read_bytes()).hexdigest()
        doc,tris,states=inspect(path)
        assert sha==m['sha256'],name+' SHA mismatch'
        assert tris==m['triangles'],name+' triangle mismatch'
        names={x.get('name') for x in doc['nodes']}
        assert set(m['nodes'])<=names
        assert set(m['animations'])=={a['name'] for a in doc.get('animations',[])}
        bounds={s:{'min':v.min(axis=0).round(6).tolist(),'max':v.max(axis=0).round(6).tolist()} for s,v in states.items()}
        assert abs(bounds['rest']['min'][1])<.001,name+' floating base'
        if 'Close' in states:
            assert np.allclose(states['rest'],states['Close'],atol=.0001),name+' rest differs from Close end'
        model={'name':name,'sha256':sha,'triangles':tris,'states':bounds,'collision_uncovered_vertices':{}}
        for state,vs in states.items():
            label={'rest':'closed','Open':'open','Close':'closed'}[state]
            declared=m['godot_bounds_open'] if label=='open' else m['godot_bounds_closed_or_rest']
            expected=np.array([[declared['min_'+a] for a in 'xyz'],[declared['max_'+a] for a in 'xyz']])
            actual=np.array([vs.min(axis=0),vs.max(axis=0)])
            if np.max(np.abs(expected-actual))>=.001:
                report['issues'].append(f'{name}/{state}: transformed GLB bounds differ from declared metadata. Actual {actual.round(6).tolist()} versus declared {expected.tolist()}.')
            shape=collisions[m['namespaced_id']]
            boxes=shape.get(label,shape.get('default'))
            if name=='oak-fence':
                boxes=[b for key in ('post','North','South','East','West') for b in shape[key]]
            if boxes:
                covered=np.zeros(len(vs),dtype=bool)
                for b in boxes:
                    c=np.array(b['center']);h=np.array(b['size'])/2
                    covered|=((vs>=c-h-.001)&(vs<=c+h+.001)).all(axis=1)
                n=int((~covered).sum());model['collision_uncovered_vertices'][state]=n
                if n:report['issues'].append(f'{name}/{state}: {n} mesh vertices outside declared collision boxes (including trim); conservative coverage is not complete.')
        if name=='wooden-chest' and bounds['Open']['max'][2]>.5 and not {(0,1,0),(0,0,1),(0,1,1)} <= {tuple(c) for c in m.get('clearance_cells',[])}:
            report['issues'].append('wooden-chest/Open: lid extends into rear neighbor (+Z) as well as upper cell; clearance_cells lists only upper cell.')
        if name=='oak-door' and bounds['Open']['min'][0]<-.501 and not m.get('clearance_cells'):
            report['issues'].append('oak-door/Open: trim extends into -X neighbor; clearance_cells is empty. Opening sweep also needs validation.')
        report['models'].append(model)
    report['binary_checks_pass']=True
    report['delivery_checks_pass']=not report['issues']
    report['ready_for_gameplay']=False
    out=ROOT/'artifacts/blocks-delivery-review.json';out.write_text(json.dumps(report,indent=2)+'\n')
    print(json.dumps({'binary_checks_pass':True,'models':len(report['models']),'issues':report['issues'],'report':str(out)},indent=2))
if __name__=='__main__':main()
