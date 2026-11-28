# -*- coding: utf-8 -*-
"""
즉흥극 지식 그래프 보기 (환각·규격 점검용).

왜: Claude가 만든 데이터 규격(과거 = 육하원칙 한 줄, PastFormat)이 실제 데이터에서 지켜지는지,
LLM이 쓴 NPC 기억이 진실(SharedPast.truth)에서 얼마나 벗어났는지를 그래프로 눈에 보이게 한다.
DB 없이 HTML 파일 하나로 만든다. 더블클릭하면 브라우저에서 열린다 (그래프 그리는 라이브러리만 인터넷에서 받는다).

읽는 것 (에디터 없이 에셋 YAML을 바로 읽는다. 읽기만 하고 고치지 않는다):
  - Assets/Data/Characters/TestCast/*.asset  : NPC 시트 (pasts = NPC가 기억하는 판본)
  - TestCastPreset.asset                      : truths = 진실 (월드 데이터, P-2)
  - Assets/Data/Generation/CharacterTables.asset : 특성과 '같이 못 붙는' 관계
  - Assets/Data/Dialogue/Words.txt            : 플레이어 허용 단어와 범주 (R-015)

쓰는 것: 같은 폴더의 improv_graph.html + 화면에 점검 요약.
"""
import glob
import json
import os
import re
import sys

import yaml

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
OUT = os.path.join(os.path.dirname(__file__), 'improv_graph.html')

# =====================================================================
# #1. 유니티 에셋 읽기
# =====================================================================

def load_asset(path):
    """유니티 YAML은 %TAG와 '--- !u!114 &번호' 줄 때문에 일반 YAML 파서가 못 읽는다. 그 줄만 걸러낸다."""
    lines = [l for l in open(path, encoding='utf-8') if not (l.startswith('%') or l.startswith('--- !u!'))]
    return yaml.safe_load(''.join(lines))['MonoBehaviour']

# =====================================================================
# #2. 규격 줄 해석 (PastFormat.Line의 거꾸로)
#   규격: [때 | 곳] 누가 → 무엇을. 어떻게: …. 왜: ….
# =====================================================================

LINE = re.compile(r'^(?:\[(?P<tw>[^\]]*)\]\s*)?(?P<who>[^→]+?)\s*→\s*(?P<rest>.+)$')

def parse_event(text):
    """한 줄을 칸으로 나눈다. 규격에 안 맞으면 None (= 규격 위반으로 보고)."""
    m = LINE.match(text.strip())
    if not m:
        return None
    rest, why, how = m.group('rest'), '', ''
    if '. 왜: ' in rest:
        rest, why = rest.split('. 왜: ', 1)
    if '. 어떻게: ' in rest:
        rest, how = rest.split('. 어떻게: ', 1)
    when = where = ''
    tw, ambiguous = m.group('tw'), False
    if tw is not None:
        if ' | ' in tw:
            when, where = [s.strip() for s in tw.split(' | ', 1)]
        else:
            # 칸이 하나뿐이면 PastFormat은 구분 없이 찍는다. 때인지 곳인지 글만 보고는 모른다 → 때로 보고 표시
            when, ambiguous = tw.strip(), True
    return dict(when=when, where=where, who=m.group('who').strip(), what=rest.strip(),
                how=how.strip(), why=why.strip(), ambiguous=ambiguous, line=text.strip())

def parse_block(text):
    """기억/진실 한 묶음. 첫 줄은 머리, 나머지는 '- '로 시작하는 줄들."""
    rows = text.split('\n')
    head, events, people, feeling, broken = rows[0].strip(), [], [], '', []
    for r in rows[1:]:
        r = r.strip()
        if not r.startswith('- '):
            broken.append(r)
            continue
        r = r[2:]
        if r.startswith('사람: '):
            people.append(r[4:])
        elif r.startswith('지금 마음: '):
            feeling = r[7:]
        else:
            e = parse_event(r)
            (events.append(e) if e else broken.append(r))
    return head, events, people, feeling, broken

# =====================================================================
# #3. 기억 ↔ 진실 맞대기
# =====================================================================

def bigrams(s):
    s = re.sub(r'\s+', '', s)
    return {s[i:i + 2] for i in range(len(s) - 1)} or {s}

def similar(a, b):
    """글자 두 개씩 겹치는 정도 (0~1). 형태소 분석 없이 '같은 일을 말하는가'를 대충 잰다."""
    x, y = bigrams(a), bigrams(b)
    return len(x & y) / len(x | y)

MATCH_AT = 0.25   # 이 이상 비슷하면 같은 일로 본다. 낮추면 너그러워진다

# =====================================================================
# #4. 그래프 모으기 (노드는 id → 정보, 선은 목록)
# =====================================================================

nodes, edges, issues = {}, [], []

def node(group, key, label, **props):
    """group은 색과 걸러 보기 단위. 같은 key를 또 부르면 속성만 더한다."""
    n = nodes.setdefault(key, dict(id=key, group=group, label=label, props={}))
    n['props'].update(props)
    return n

def rel(a, typ, b, **props):
    edges.append(dict(source=a, target=b, type=typ, props=props))

def actor_key(who, cast):
    """누가 칸 → 사람 노드. 명단에도 '사람:' 줄에도 없으면 '모르는 사람' (지어낸 인물 의심)."""
    if who in cast:
        return 'P:' + who
    if 'O:' + who in nodes:
        return 'O:' + who
    node('Unknown', 'U:' + who, who, 설명='명단에도 사람: 줄에도 없는 이름')
    return 'U:' + who

def add_event(key, e, label, **extra):
    props = {k: e[k] for k in ('line', 'when', 'where', 'who', 'what', 'how', 'why') if e[k]}
    if e['ambiguous']:
        props['규격'] = '[ ] 안 칸이 하나뿐: 때인지 곳인지 모름'
    node('Event', key, label, **props, **extra)
    if e['where']:
        node('Place', 'L:' + e['where'], e['where'])
        rel(key, 'AT', 'L:' + e['where'])

def build():
    # ---------- 사람 ----------
    sheets = {}
    for path in sorted(glob.glob(os.path.join(ROOT, 'Assets/Data/Characters/TestCast/*.asset'))):
        d = load_asset(path)
        if 'displayName' in d:
            sheets[d['displayName']] = d
    cast = set(sheets)
    for name in cast:
        node('Person', 'P:' + name, name, 소개=sheets[name].get('publicProfile') or '')

    # ---------- 진실 ----------
    preset = load_asset(os.path.join(ROOT, 'Assets/Data/Characters/TestCast/TestCastPreset.asset'))
    truth_events = {}
    for block in preset.get('truths') or []:
        head, events, people, _, broken = parse_block(block)
        m = re.match(r'\[(.+?) - (.+?)\]\s*(.*)', head)
        if not m:
            issues.append(('규격', f'진실 머리 규격 위반: {head}'))
            continue
        a, b, tail = m.group(1), m.group(2), m.group(3)
        kind = re.sub(r',?\s*(짙음|가벼움)$', '', tail).strip()
        pk = f'S:{min(a, b)}~{max(a, b)}'
        node('SharedPast', pk, f'{kind}\n({a}·{b})', 종류=kind, 짙음='짙음' in tail)
        for who in (a, b):
            if who not in cast:
                issues.append(('환각', f'진실에 명단 밖 당사자: {who}'))
                node('Unknown', 'P:' + who, who)
            rel(pk, 'BETWEEN', 'P:' + who)
        if '가벼움' in tail:
            issues.append(('규격', f'[{a}~{b}] 진실 머리에 "가벼움"이 있다. 지금 PastFormat.Truth는 짙음만 찍는다 (옛 규격이 남은 데이터)'))
        if people:
            issues.append(('규격', f'[{a}~{b}] 진실에 "사람:" 줄이 있다. 지금 PastFormat.Truth는 사람 줄을 찍지 않는다 (옛 규격)'))
        for p in people:
            nm = p.split('=')[0].strip()
            node('Other', 'O:' + nm, nm, 설명=p)
            rel(pk, 'MENTIONS', 'O:' + nm)
        for line in broken:
            issues.append(('규격', f'[{a}~{b}] 진실의 규격 위반 줄: {line}'))
        lst = truth_events.setdefault(pk, [])
        for i, e in enumerate(events):
            ek = f'T:{pk}:{i}'
            lst.append((ek, e))
            add_event(ek, e, f'진실 {i + 1}\n{e["who"]} → {e["what"][:14]}', 출처='진실')
            rel(ek, 'IN_TRUTH_OF', pk)
            rel(ek, 'BY', actor_key(e['who'], cast))

    # ---------- 기억 (NPC마다 자기 판본) ----------
    for name, d in sheets.items():
        for block in d.get('pasts') or []:
            head, events, people, feeling, broken = parse_block(block)
            m = re.match(r'\[(.+?)와\(과\)의 과거\]', head)
            if not m:
                issues.append(('규격', f'{name}의 기억 머리 규격 위반: {head}'))
                continue
            other = m.group(1)
            pk = f'S:{min(name, other)}~{max(name, other)}'
            vk = f'V:{name}:{other}'
            node('Version', vk, f'{name}의 기억\n({other})', 지금마음=feeling)
            rel('P:' + name, 'REMEMBERS', vk)
            if pk in truth_events:
                rel(vk, 'VERSION_OF', pk)
            else:
                issues.append(('환각', f'{name}가 {other}와의 과거를 기억하는데 진실이 없다 (기억만 있는 과거)'))
            for p in people:
                nm = p.split('=')[0].strip()
                node('Other', 'O:' + nm, nm, 설명=p)
                rel(vk, 'MENTIONS', 'O:' + nm)
            for line in broken:
                issues.append(('규격', f'{name}의 기억 규격 위반 줄: {line}'))
            truths = truth_events.get(pk, [])
            mine = []
            for i, e in enumerate(events):
                ek = f'{vk}:{i}'
                actor = name if e['who'] == '나' else e['who']   # 기억 속 "나" = 기억하는 사람 (PastVersion)
                mine.append((actor, e))
                best, score = None, 0.0
                for tk, t in truths:
                    if t['who'] != actor:
                        continue   # 주어가 다르면 같은 일이 아니다 (주어 혼동을 막으려는 규격의 핵심)
                    s = similar(e['what'], t['what'])
                    if s > score:
                        best, score = tk, s
                matched = best is not None and score >= MATCH_AT
                add_event(ek, e, f'{name} 기억 {i + 1}\n{e["who"]} → {e["what"][:14]}', 출처=f'{name}의 기억')
                rel(vk, 'HAS', ek)
                rel(ek, 'BY', actor_key(actor, cast))
                if matched:
                    rel(ek, 'MATCHES', best, 비슷함=round(score, 2))
                else:
                    nodes[ek]['suspect'] = True
                    issues.append(('환각', f'진실에 없는 기억 ({name}): {e["line"]}'))
            # 당사자인데 기억에서 빠진 진실 (잊었거나 숨김. 성격 탓 왜곡일 수도 있다)
            for tk, t in truths:
                if t['who'] == name and not any(a == name and similar(e['what'], t['what']) >= MATCH_AT for a, e in mine):
                    rel(vk, 'OMITS', tk)
                    issues.append(('누락', f'{name}가 자기 일을 기억에서 뺐다: {t["line"]}'))

    for n in nodes.values():
        if n['group'] == 'Event' and any(e['source'] == n['id'] and e['type'] == 'BY' and e['target'].startswith('U:') for e in edges):
            n['suspect'] = True

    # ---------- 특성 ----------
    tables = load_asset(os.path.join(ROOT, 'Assets/Data/Generation/CharacterTables.asset'))
    traits = {t['name']: t for t in tables.get('traits') or []}
    for nm, t in traits.items():
        node('Trait', 'R:' + nm, nm, 설명=t.get('description') or '', 집중도=float(t.get('attention') or 0))
    for nm, t in traits.items():
        for c in t.get('conflicts') or []:
            if c not in traits:
                issues.append(('데이터', f'특성 "{nm}"이 없는 특성 "{c}"와 충돌한다고 적혀 있다'))
                node('Unknown', 'R:' + c, c)
            elif nm not in (traits[c].get('conflicts') or []):
                issues.append(('데이터', f'특성 충돌이 한쪽에만 있다: {nm} → {c}'))
            rel('R:' + nm, 'CONFLICTS', 'R:' + c)

    # ---------- 허용 단어 (R-015) ----------
    cat, seen = '(범주 없음)', {}
    for raw in open(os.path.join(ROOT, 'Assets/Data/Dialogue/Words.txt'), encoding='utf-8'):
        line = raw.strip()
        if not line:
            continue
        if line.startswith('#'):
            label = line[1:].strip()
            if label and len(label) <= 12 and ' ' not in label:   # 짧은 한 낱말만 범주 이름, 긴 줄은 설명
                cat = label
                node('WordCategory', 'C:' + cat, cat)
            continue
        if line in seen:
            # WordDictionary.Parse는 두 번째 것을 조용히 버린다 → 범주가 둘인 단어는 앞 범주에만 남는다
            issues.append(('데이터', f'단어 "{line}"가 {seen[line]}와 {cat}에 두 번 있다 (뒤의 것은 무시됨)'))
            continue
        seen[line] = cat
        node('Word', 'W:' + line, line)
        rel('C:' + cat, 'HAS', 'W:' + line)

# =====================================================================
# #5. HTML 쓰기 (그림은 vis-network가 그린다)
# =====================================================================

PAGE = r'''<!doctype html>
<html lang="ko"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>즉흥극 지식 그래프</title>
<script src="https://unpkg.com/vis-network@9.1.9/standalone/umd/vis-network.min.js"></script>
<style>
:root { --bg:#fafaf8; --panel:#fff; --ink:#222; --muted:#777; --line:#ddd; --bad:#d33; }
@media (prefers-color-scheme: dark) { :root { --bg:#1b1b1d; --panel:#242427; --ink:#eee; --muted:#999; --line:#3a3a3e; --bad:#ff6b6b; } }
* { box-sizing:border-box; }
body { margin:0; font:14px/1.5 "Noto Sans KR", system-ui, sans-serif; background:var(--bg); color:var(--ink); display:flex; height:100vh; }
#side { width:380px; overflow:auto; padding:16px; border-right:1px solid var(--line); background:var(--panel); }
#graph { flex:1; }
h1 { font-size:17px; margin:0 0 8px; } h2 { font-size:14px; margin:16px 0 6px; }
label { display:inline-block; margin:2px 10px 2px 0; cursor:pointer; }
.issue { padding:6px 8px; margin:4px 0; border-left:3px solid var(--line); font-size:13px; }
.issue b { color:var(--muted); font-weight:600; margin-right:4px; }
.issue.환각 { border-color:var(--bad); } .issue.누락 { border-color:#e6a23c; }
#info { white-space:pre-wrap; font-size:13px; background:var(--bg); padding:8px; border-radius:6px; min-height:40px; }
.k { color:var(--muted); }
@media (max-width: 760px) { body { flex-direction:column; } #side { width:100%; height:45vh; border-right:0; border-bottom:1px solid var(--line); } }
</style></head><body>
<div id="side">
  <h1>즉흥극 지식 그래프</h1>
  <div>빨간 테두리 = 진실과 안 맞는 기억(환각 의심). 점선 주황 = 당사자가 기억에서 뺀 진실.</div>
  <h2>보기</h2><div id="filters"></div>
  <h2>누른 것</h2><div id="info">노드나 선을 누르면 칸별 내용이 나온다.</div>
  <h2>점검 거리 (<span id="n"></span>)</h2><div id="issues"></div>
</div>
<div id="graph"></div>
<script>
const DATA = __DATA__;
const SETS = { '과거와 기억': ['Person','SharedPast','Version','Event','Other','Unknown','Place'], '특성 충돌': ['Trait'], '허용 단어': ['WordCategory','Word'] };
const COLOR = { Person:'#4e79a7', SharedPast:'#9c755f', Version:'#76b7b2', Event:'#bab0ac', Other:'#b07aa1', Unknown:'#e15759', Place:'#8cd17d', Trait:'#f28e2b', WordCategory:'#59a14f', Word:'#c9e4c0' };
const SHAPE = { Person:'dot', SharedPast:'diamond', Version:'box', Event:'box', Place:'triangle', Unknown:'star', WordCategory:'hexagon' };
const EDGE = { MATCHES:{color:'#59a14f', width:2}, OMITS:{color:'#e6a23c', dashes:true, width:2}, CONFLICTS:{color:'#e15759'}, BY:{color:'#4e79a7'} };
const on = { '과거와 기억': true, '특성 충돌': false, '허용 단어': false };
const nodes = new vis.DataSet(), edges = new vis.DataSet();
function groupOn(g) { return Object.keys(SETS).some(s => on[s] && SETS[s].includes(g)); }
function draw() {
  const ids = new Set(DATA.nodes.filter(n => groupOn(n.group)).map(n => n.id));
  nodes.clear(); edges.clear();
  nodes.add(DATA.nodes.filter(n => ids.has(n.id)).map(n => ({ id:n.id, label:n.label, shape:SHAPE[n.group]||'dot', size:n.group==='Word'?6:14,
    color:{ background:COLOR[n.group], border:n.suspect?'#d33':COLOR[n.group] }, borderWidth:n.suspect?4:1,
    font:{ color: getComputedStyle(document.body).color, size: n.group==='Word'?10:13, multi:false } })));
  edges.add(DATA.edges.filter(e => ids.has(e.source) && ids.has(e.target)).map((e,i) => Object.assign({ id:i, from:e.source, to:e.target, label:e.type,
    arrows:'to', font:{ size:9, color:'#888', strokeWidth:0 }, color:'#aaa' }, EDGE[e.type]||{})));
}
const fl = document.getElementById('filters');
for (const s of Object.keys(SETS)) { const l = document.createElement('label'); l.innerHTML = `<input type="checkbox" ${on[s]?'checked':''}> ${s}`;
  l.firstChild.onchange = ev => { on[s] = ev.target.checked; draw(); }; fl.appendChild(l); }
const net = new vis.Network(document.getElementById('graph'), { nodes, edges }, { physics:{ solver:'forceAtlas2Based', stabilization:{ iterations:300 } }, interaction:{ hover:true } });
const byId = Object.fromEntries(DATA.nodes.map(n => [n.id, n]));
const esc = s => String(s).replace(/[&<>]/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;'}[c]));
function show(title, props) { document.getElementById('info').innerHTML = `<b>${esc(title)}</b>\n` + Object.entries(props).map(([k,v]) => `<span class="k">${esc(k)}:</span> ${esc(v)}`).join('\n'); }
net.on('click', p => {
  if (p.nodes.length) { const n = byId[p.nodes[0]]; show(n.label.replace('\n',' '), Object.assign({ 종류:n.group }, n.suspect ? { 점검:'진실과 안 맞음 / 모르는 사람' } : {}, n.props)); }
  else if (p.edges.length) { const e = DATA.edges[p.edges[0]]; show(e.type, Object.assign({ 에서:byId[e.source].label, 으로:byId[e.target].label }, e.props)); }
});
document.getElementById('n').textContent = DATA.issues.length;
document.getElementById('issues').innerHTML = DATA.issues.map(([k,t]) => `<div class="issue ${k}"><b>${k}</b>${esc(t)}</div>`).join('');
draw();
</script></body></html>
'''

def main():
    build()
    data = dict(nodes=list(nodes.values()), edges=edges, issues=issues)
    # </script>가 데이터 안에 나와 스크립트가 끊기지 않게 '/'를 이스케이프
    blob = json.dumps(data, ensure_ascii=False).replace('</', '<\\/')
    open(OUT, 'w', encoding='utf-8', newline='\n').write(PAGE.replace('__DATA__', blob))
    print(f'노드 {len(nodes)}개, 선 {len(edges)}개 → {OUT}')
    print(f'점검 거리 {len(issues)}개:')
    for k, s in issues:
        print(f' - [{k}] {s}')

if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8')
    main()
