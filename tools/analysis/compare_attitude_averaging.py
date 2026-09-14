import numpy as np

# ENU-ish frame: east=(1,0,0), north=(0,1,0), up=(0,0,1); azimuth clockwise from north.
def pole(dip_deg, az_deg):
    d, a = np.radians(dip_deg), np.radians(az_deg)
    return np.array([-np.sin(d)*np.sin(a), -np.sin(d)*np.cos(a), np.cos(d)])

def to_dip_az(n):
    n = n/np.linalg.norm(n)
    if n[2] < 0: n = -n                      # orient up
    dip = np.degrees(np.arccos(np.clip(n[2],-1,1)))
    az  = (np.degrees(np.arctan2(-n[0], -n[1]))) % 360
    return dip, az

def method_A(poles, orient_up=True):          # Fisher / mean unit normal
    P = np.array([p if (not orient_up or p[2] >= 0) else -p for p in poles])
    s = P.sum(axis=0); R = np.linalg.norm(s)/len(P)
    return to_dip_az(s), R

def method_B(poles):                          # circular mean az + arithmetic mean dip
    das = [to_dip_az(p) for p in poles]
    dips = np.array([d for d,_ in das]); azs = np.radians([a for _,a in das])
    Rz = np.hypot(np.sin(azs).mean(), np.cos(azs).mean())
    az = np.degrees(np.arctan2(np.sin(azs).sum(), np.cos(azs).sum())) % 360
    return (dips.mean(), az), Rz

def method_C(poles):                          # orientation tensor / Bingham (axial)
    T = sum(np.outer(p,p) for p in poles)/len(poles)
    w, v = np.linalg.eigh(T); idx = np.argsort(w)[::-1]
    w, v = w[idx], v[:,idx]
    return to_dip_az(v[:,0]), w                # w = S1>=S2>=S3, sum to 1

def show(name, dipaz_list, flip=None):
    poles = [pole(d,a) for d,a in dipaz_list]
    if flip:
        for i in flip: poles[i] = -poles[i]    # simulate uncorrected regression sign
    (dA,aA),R  = method_A(poles)
    (dB,aB),Rz = method_B(poles)
    (dC,aC),w  = method_C(poles)
    print(f"\n{name}")
    print(f"  input        {dipaz_list}" + (f"  (normals flipped at {flip})" if flip else ""))
    print(f"  A vector mean   dip {dA:6.2f}  az {aA:7.2f}   R  = {R:.4f}")
    print(f"  B az+dip mean   dip {dB:6.2f}  az {aB:7.2f}   Rz = {Rz:.4f}")
    print(f"  C orient.tensor dip {dC:6.2f}  az {aC:7.2f}   S = {np.round(w,4)}")

show("1. shallow, opposing dips (5/000 and 5/180) - true mean is horizontal",
     [(5,0),(5,180)])
show("2. same near-vertical bed, fitted normals came out opposite",
     [(88,90),(88,90)], flip=[1])
show("3. tight cluster (the benign case)",
     [(30,120),(32,124),(29,117),(31,122),(30,119)])
show("4. fold: two limbs 40/090 and 40/270 - no single mean plane exists",
     [(40,90),(41,88),(39,92),(40,270),(41,272),(39,268)])
show("5. moderate spread in azimuth at moderate dip",
     [(40,60),(40,120),(35,90)])
show("6. shallow beds, moderate azimuth spread (60 deg) at dip 8",
     [(8,60),(8,120),(8,90)])

print("\n" + "="*78)
show("7. near-vertical bed, two measurements leaning a hair either side (89.9/090, 89.9/270)",
     [(89.9,90),(89.9,270)])
show("8. same, but 8 measurements scattered about vertical",
     [(89.5,90),(89.7,270),(89.9,90),(88.9,270),(89.2,90),(89.6,270),(89.8,90),(89.4,270)])

def woodcock(poles):
    T = sum(np.outer(p,p) for p in poles)/len(poles)
    w = np.sort(np.linalg.eigvalsh(T))[::-1]
    w = np.clip(w, 1e-12, None)
    K = np.log(w[0]/w[1])/np.log(w[1]/w[2])
    C = np.log(w[0]/w[2])
    return w, K, C

print("\n" + "="*78)
print("Woodcock shape parameters  (K > 1 cluster, K < 1 girdle; C = strength)")
for name, dat in [
    ("tight cluster           ", [(30,120),(32,124),(29,117),(31,122),(30,119)]),
    ("moderate cluster        ", [(40,60),(40,120),(35,90)]),
    ("shallow opposing dips   ", [(5,0),(5,180)]),
    ("fold / girdle           ", [(40,90),(41,88),(39,92),(40,270),(41,272),(39,268)]),
    ("random-ish scatter      ", [(10,0),(70,90),(45,200),(80,300),(20,140),(60,20)]),
]:
    P=[pole(d,a) for d,a in dat]
    w,K,C = woodcock(P)
    print(f"  {name} S = {np.round(w,4)}   K = {K:7.3f}   C = {C:7.3f}")

# fold axis from the smallest eigenvector
P=[pole(d,a) for d,a in [(40,90),(41,88),(39,92),(40,270),(41,272),(39,268)]]
T = sum(np.outer(p,p) for p in P)/len(P)
w,v = np.linalg.eigh(T); idx=np.argsort(w)
axis = v[:,idx[0]]
if axis[2] < 0: axis = -axis
plunge = np.degrees(np.arcsin(np.clip(axis[2],-1,1)))
trend  = np.degrees(np.arctan2(axis[0], axis[1])) % 360
print(f"\n  fold axis (smallest eigenvector): trend {trend:.1f}  plunge {plunge:.1f}  (expected ~000/180, plunge 0)")
