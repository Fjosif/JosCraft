"""Independent binary GLB inspection; does not trust the authoring manifest."""
import json, struct, hashlib, pathlib, math
import numpy as np

ROOT = pathlib.Path(__file__).resolve().parents[1]
REQUIRED = ['Idle','Walk','Run','Jump','Attack','Hit','Sit','Ride']
IDS = ['tiger','fox','dragon','unicorn','robot','owl','panda','cat','bunny','bear','octo']

def inspect(path):
    data=path.read_bytes()
    magic,version,length=struct.unpack_from('<III',data)
    assert magic==0x46546c67 and version==2 and length==len(data)
    p=12; binary=b''; doc=None
    while p<len(data):
        size,kind=struct.unpack_from('<II',data,p); p+=8
        chunk=data[p:p+size];p+=size
        if kind==0x4e4f534a: doc=json.loads(chunk)
        elif kind==0x004e4942: binary=chunk
    def access(i):
        a=doc['accessors'][i];v=doc['bufferViews'][a['bufferView']]
        n={'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4,'MAT4':16}[a['type']]
        kind={5120:'b',5121:'B',5122:'h',5123:'H',5125:'I',5126:'f'}[a['componentType']]
        fmt='<'+kind*n;size=struct.calcsize(fmt);stride=v.get('byteStride',size)
        start=v.get('byteOffset',0)+a.get('byteOffset',0)
        return [struct.unpack_from(fmt,binary,start+j*stride) for j in range(a['count'])]
    meshes=doc.get('meshes',[]);verts=[];tris=0
    for m in meshes:
        for prim in m['primitives']:
            vv=access(prim['attributes']['POSITION']);verts+=vv
            tris+=(doc['accessors'][prim['indices']]['count'] if 'indices' in prim else len(vv))//3
            assert prim.get('mode',4)==4
            assert 'JOINTS_0' in prim['attributes'] and 'WEIGHTS_0' in prim['attributes'], 'unskinned primitive'
            weights=access(prim['attributes']['WEIGHTS_0'])
            assert all(abs(sum(w)-1)<0.002 for w in weights), 'invalid weights'
    assert verts and all(math.isfinite(c) for v in verts for c in v)
    clips={}
    for anim in doc.get('animations',[]):
        changed=[]; hashes=[];keyframes=0;duration=0
        for chan in anim['channels']:
            sampler=anim['samplers'][chan['sampler']]
            values=access(sampler['output']);times=access(sampler['input'])
            keyframes+=len(times);duration=max(duration,max(t[0] for t in times))
            amplitude=max((max(row[k] for row in values)-min(row[k] for row in values) for k in range(len(values[0]))),default=0)
            if amplitude>1e-5: changed.append({'node':doc['nodes'][chan['target']['node']].get('name'),'path':chan['target']['path'],'amplitude':round(amplitude,5)})
            hashes.append((chan['target'],values))
        clips[anim['name']]={'duration':duration,'channels':len(anim['channels']),'keyframes':keyframes,'varying_channels':changed,'signature':hashlib.sha256(json.dumps(hashes,sort_keys=True).encode()).hexdigest()}
    assert set(REQUIRED)<=set(clips),f'missing clips {set(REQUIRED)-set(clips)}'
    assert all(clips[k]['varying_channels'] for k in REQUIRED),'empty animation'
    assert len({clips[k]['signature'] for k in REQUIRED})==8,'duplicate animation data'
    joints=[doc['nodes'][i].get('name') for s in doc.get('skins',[]) for i in s['joints']]
    assert joints,'missing joints'
    def trs(t,r,s):
        x,y,z,w=r
        rotation=np.array([[1-2*y*y-2*z*z,2*x*y-2*z*w,2*x*z+2*y*w],[2*x*y+2*z*w,1-2*x*x-2*z*z,2*y*z-2*x*w],[2*x*z-2*y*w,2*y*z+2*x*w,1-2*x*x-2*y*y]])
        m=np.eye(4);m[:3,:3]=rotation@np.diag(s);m[:3,3]=t;return m
    parents={child:i for i,n in enumerate(doc['nodes']) for child in n.get('children',[])}
    def sample(anim,time):
        props={i:{'translation':n.get('translation',[0,0,0]),'rotation':n.get('rotation',[0,0,0,1]),'scale':n.get('scale',[1,1,1])} for i,n in enumerate(doc['nodes'])}
        for channel in anim['channels']:
            sp=anim['samplers'][channel['sampler']];times=np.array(access(sp['input']))[:,0];values=np.array(access(sp['output']))
            assert sp.get('interpolation','LINEAR') in ('LINEAR','STEP')
            i=max(0,min(len(times)-2,int(np.searchsorted(times,time,side='right')-1)));f=0 if len(times)<2 or sp.get('interpolation')=='STEP' else max(0,min(1,(time-times[i])/(times[i+1]-times[i])))
            a=values[i];b=values[min(i+1,len(values)-1)];target=channel['target']
            if target['path']=='rotation' and np.dot(a,b)<0:b=-b
            value=a*(1-f)+b*f
            if target['path']=='rotation':value/=np.linalg.norm(value)
            props[target['node']][target['path']]=value
        glob={}
        def global_m(i):
            if i not in glob:
                p=props[i];m=np.array(doc['nodes'][i]['matrix']).reshape((4,4),order='F') if 'matrix' in doc['nodes'][i] else trs(p['translation'],p['rotation'],p['scale'])
                glob[i]=global_m(parents[i])@m if i in parents else m
            return glob[i]
        world=[]
        for node in doc['nodes']:
            if 'mesh' not in node or 'skin' not in node:continue
            skin=doc['skins'][node['skin']];ibms=[np.array(m).reshape((4,4),order='F') for m in access(skin['inverseBindMatrices'])]
            transforms=np.array([global_m(j)@ibm for j,ibm in zip(skin['joints'],ibms)])
            for primitive in doc['meshes'][node['mesh']]['primitives']:
                a=primitive['attributes'];v=np.c_[np.array(access(a['POSITION'])),np.ones(doc['accessors'][a['POSITION']]['count'])];j=np.array(access(a['JOINTS_0']),dtype=int);w=np.array(access(a['WEIGHTS_0']))
                blended=(transforms[j]*w[:,:,None,None]).sum(axis=1)
                world.append(np.einsum('nij,nj->ni',blended,v)[:,:3])
        return np.concatenate(world)
    cpu={}
    for anim in doc['animations']:
        d=clips[anim['name']]['duration'];samples=[sample(anim,d*f/24) for f in range(25)]
        cpu[anim['name']]={'sample_count':len(samples),'min_y':float(min(v[:,1].min() for v in samples)),'max_min_y':float(max(v[:,1].min() for v in samples)),'vertex_movement_max':float(max(np.linalg.norm(v-samples[0],axis=1).max() for v in samples)),'method':'independent GLB CPU skinning at25 temporal samples; reference plane y0, not rendered/physics proof'}
        assert cpu[anim['name']]['vertex_movement_max']>0.0001,'animation does not move actual skinned vertices'
    return {'id':path.stem,'sha256':hashlib.sha256(data).hexdigest(),'bytes':len(data),'meshes':len(meshes),'primitives':sum(len(m['primitives']) for m in meshes),'vertices':len(verts),'triangles':tris,'materials':len(doc.get('materials',[])),'bones':joints,'bind_mesh_bounds':{'min':[min(v[k] for v in verts) for k in range(3)],'max':[max(v[k] for v in verts) for k in range(3)]},'clips':clips,'cpu_skin_samples':cpu,'pass':True}

if __name__=='__main__':
    results=[]
    for ident in IDS:
        try: results.append(inspect(ROOT/'assets'/'characters'/f'{ident}.glb'))
        except Exception as e: results.append({'id':ident,'pass':False,'error':str(e)})
    report={'scope':'independent binary GLB data inspection; visual and runtime grounding verified separately','all_pass':all(r['pass'] for r in results),'models':results}
    (ROOT/'artifacts'/'qa-glb.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
    print(json.dumps({'all_pass':report['all_pass'],'models':[{k:r[k] for k in ['id','pass','triangles','materials','error'] if k in r} for r in results]},indent=2))
    raise SystemExit(0 if report['all_pass'] else 1)
