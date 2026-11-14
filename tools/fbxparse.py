import struct, zlib, sys
def load(path):
    d=open(path,"rb").read(); ver=struct.unpack_from("<I",d,23)[0]; big=ver>=7500
    def prop(p):
        t=chr(d[p]); p+=1
        if t in "YCFDIL":
            f={'Y':'<h','C':'<B','F':'<f','D':'<d','I':'<i','L':'<q'}[t]; return struct.unpack_from(f,d,p)[0],p+struct.calcsize(f)
        if t in "fdilb":
            ln,enc,cl=struct.unpack_from("<III",d,p); p+=12; raw=d[p:p+cl]
            if enc: raw=zlib.decompress(raw)
            f={'f':'f','d':'d','i':'i','l':'q','b':'B'}[t]; return list(struct.unpack("<%d%s"%(ln,f),raw)),p+cl
        ln=struct.unpack_from("<I",d,p)[0]; p+=4; v=d[p:p+ln]; return (v.decode("latin1") if t=="S" else v),p+ln
    H=25 if big else 13
    def node(p):
        if big: end,np,pl=struct.unpack_from("<QQQ",d,p); nl=d[p+24]
        else: end,np,pl=struct.unpack_from("<III",d,p); nl=d[p+12]
        if end==0: return None
        name=d[p+H:p+H+nl].decode(); q=p+H+nl; props=[]
        for _ in range(np): v,q=prop(q); props.append(v)
        kids=[]
        while q<end:
            k=node(q)
            if k is None: q+=H; break
            kids.append(k[0]); q=k[1]
        return (name,props,kids),end
    p=27; top=[]
    while p<len(d)-200:
        r=node(p)
        if r is None: break
        top.append(r[0]); p=r[1]
    return ver,top
def child(n,name): return [k for k in n[2] if k[0]==name]
def props70(n):
    out={}
    for k in child(n,"Properties70"):
        for pp in k[2]: out[pp[1][0]]=pp[1][4:]
    return out
