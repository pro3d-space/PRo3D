import numpy as np
rng = np.random.default_rng(7)

def pole(d,a):
    d,a=np.radians(d),np.radians(a)
    return np.array([-np.sin(d)*np.sin(a),-np.sin(d)*np.cos(a),np.cos(d)])

def S(poles):
    T=sum(np.outer(p,p) for p in poles)/len(poles)
    return np.sort(np.linalg.eigvalsh(T))[::-1]

def scattered(mean_dip, mean_az, sigma_deg, n=30):
    m = pole(mean_dip, mean_az)
    # build an orthonormal frame around m and perturb
    a = np.array([0,0,1.0]) if abs(m[2])<0.9 else np.array([1.0,0,0])
    u = np.cross(m,a); u/=np.linalg.norm(u); v=np.cross(m,u)
    out=[]
    for _ in range(n):
        th = np.radians(rng.normal(0,sigma_deg)); ph = rng.uniform(0,2*np.pi)
        out.append(m*np.cos(th) + (u*np.cos(ph)+v*np.sin(ph))*np.sin(th))
    return out

print("angular scatter of poles  ->  S1 (largest normalised eigenvalue), n=30")
for s in [2,5,10,15,20,25,30,40,60]:
    vals=[S(scattered(35,120,s))[0] for _ in range(200)]
    v2 =[S(scattered(35,120,s))[1] for _ in range(200)]
    print(f"  sigma = {s:3d} deg   S1 = {np.mean(vals):.3f}   S2/S1 = {np.mean(v2)/np.mean(vals):.3f}")

print("\nuniformly random poles (no structure at all), n=30, 200 trials")
def uniform_poles(n=30):
    v = rng.normal(size=(n,3)); v/= np.linalg.norm(v,axis=1)[:,None]; return list(v)
vals=[S(uniform_poles())[0] for _ in range(200)]
print(f"  S1 = {np.mean(vals):.3f} +- {np.std(vals):.3f}   (max over trials {np.max(vals):.3f})")

print("\ntwo-limb fold, varying interlimb separation (dip 40, azimuths d apart), n=30")
for sep in [10,20,40,60,90,120,180]:
    P = scattered(40,90,4,15)+scattered(40,90+sep,4,15)
    s=S(P); print(f"  separation {sep:3d} deg   S = {np.round(s,3)}   S2/S1 = {s[1]/s[0]:.3f}")
