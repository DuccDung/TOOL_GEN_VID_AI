import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { spawn } from 'node:child_process';

const output = path.resolve('.tmp/mixer-right-alignment-verification');
const assets = path.resolve('TOOL-LOCAL/Web/dist/assets');
const names = fs.readdirSync(assets);
const script = names.find(name => name.endsWith('.js'));
const style = names.find(name => name.endsWith('.css'));
const metrics = new Map();
const dashboard = {
  profile: { userId: 'fixture', email: 'fixture@example.invalid', displayName: 'Kiểm thử giao diện', accountStatus: 'Active', roles: [] },
  organizations: [], selectedOrganizationId: 'organization', projects: [], selectedProject: null,
  assetLibrary: null, models: [], generationRunning: false,
  providerStatus: { openAiReady: false, openAiVoiceOptions: [], klingReady: false, videoReady: false },
  mediaTools: { ready: true, message: 'Fixture', checkedAtUtc: '' },
  features: { vietsubEnabled: true, speechSynchronizationEnabled: false }, sceneFirstFrames: [], contentLanguageFailure: null
};
const project = {
  projectId: 'project', name: 'Video giới thiệu sản phẩm', status: 'READY', sourceLanguageCode: 'en', targetLanguageCode: 'vi',
  updatedAtUtc: '2026-09-10T00:00:00Z', needsRecovery: false, serverSynchronized: true
};
const workspace = { activeTrackId: 'track', tracks: [{ trackId: 'track', revision: 1, displayName: 'Phụ đề tiếng Việt',
  source: 'IMPORTED_SRT', languageCode: 'en', cueCount: 7, translatedCueCount: 7, warningCueCount: 0, updatedAtUtc: '' }] };
const page = { trackId: 'track', trackRevision: 1, offset: 0, pageSize: 50, totalCount: 7, search: '', status: 'ALL', speaker: '', speakers: [],
  cues: Array.from({length: 7}, (_, index) => ({cueId: `cue-${index}`, cueIndex: index,
    startMilliseconds: index * 1200, endMilliseconds: index * 1200 + 1000,
    originalText: 'Welcome to our store.', translatedText: 'Chào mừng bạn đến với cửa hàng.', speaker: 'speaker_1',
    originalLocked: false, translationLocked: false, warnings: [], updatedAtUtc: '' })) };

function html(mode, zoom) {
  return `<!doctype html><html lang="vi"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
  <link rel="stylesheet" href="/${style}"><div id="root"></div><script>
  const listeners = new Set(); let open = true; let closes = 0;
  const project = ${JSON.stringify(project)}, workspace = ${JSON.stringify(workspace)}, page = ${JSON.stringify(page)};
  const emit = data => listeners.forEach(listener => listener({data}));
  const state = requestId => emit({type:'vietsub.state',requestId,payload:{enabled:true,busy:false,projects:[project],
    selectedProject:open?project:null,subtitleWorkspace:open?workspace:null,jobs:[]}});
  window.chrome = window.chrome || {}; window.chrome.webview = {
    addEventListener: (_, callback) => listeners.add(callback), removeEventListener: (_, callback) => listeners.delete(callback),
    postMessage: raw => { const message = JSON.parse(raw); setTimeout(() => {
      if (message.type === 'app.ready') emit({type:'dashboard.state',payload:${JSON.stringify(dashboard)}});
      if (message.type === 'vietsub.state.get') state(message.requestId);
      if (message.type === 'vietsub.subtitle.page.get') emit({type:'vietsub.subtitle.page',requestId:message.requestId,payload:page});
      if (message.type === 'vietsub.project.close') { closes++; open=false; state(message.requestId);
        emit({type:'vietsub.operation.completed',requestId:message.requestId,payload:{completed:true}}); }
    }, 20); }
  };
  document.documentElement.style.zoom = ${zoom};
  const wait = ms => new Promise(resolve => setTimeout(resolve, ms));
  const until = async predicate => { for(let i=0;i<80;i++){if(predicate())return;await wait(50);}throw Error('UI did not become ready'); };
  async function inspect(){
    try {
      await until(() => [...document.querySelectorAll('.sidebar-nav button')].some(button => button.textContent.trim()==='Dịch phụ đề'));
      [...document.querySelectorAll('.sidebar-nav button')].find(button => button.textContent.trim()==='Dịch phụ đề').click();
      await until(() => document.querySelector('.vietsub-timeline-toolbar'));
      await document.fonts.ready; await wait(100);
      const toolbar=document.querySelector('.vietsub-timeline-toolbar').getBoundingClientRect();
      const mixer=document.querySelector('.vietsub-timeline-audio-mixer').getBoundingClientRect();
      const tools=document.querySelector('.vietsub-timeline-tools').getBoundingClientRect();
      const stacked=mixer.top>=tools.bottom-1;
      const gap=(tools.left-mixer.right)/${zoom};
      if(!stacked&&(gap<0||gap>13))throw Error('Mixer must sit next to timeline tools: '+gap);
      if(mixer.left<toolbar.left-1||mixer.right>toolbar.right+1||tools.right>toolbar.right+1)throw Error('Toolbar overflow');
      const result={mode:${JSON.stringify(mode)},zoom:${zoom},gapCssPixels:stacked?null:gap,stacked,mixerWidth:mixer.width/${zoom}};
      await fetch('/metrics?mode='+${JSON.stringify(mode)},{method:'POST',body:JSON.stringify(result)});
    }catch(error){await fetch('/metrics?mode='+${JSON.stringify(mode)},{method:'POST',body:JSON.stringify({error:String(error)})});}
  }
  inspect();</script><script type="module" src="/${script}"></script></html>`;
}
const server = http.createServer((req, res) => {
  const url = new URL(req.url, 'http://localhost');
  if (url.pathname === '/metrics') {
    let body=''; req.on('data', chunk => {body+=chunk;});
    req.on('end', () => { metrics.set(url.searchParams.get('mode'), JSON.parse(body)); res.end('ok'); }); return;
  }
  if (url.pathname === '/') { res.setHeader('Content-Type','text/html; charset=utf-8'); res.end(html(url.searchParams.get('mode'),Number(url.searchParams.get('zoom')||1))); return; }
  const name=url.pathname.slice(1);
  if(name!==script&&name!==style){res.writeHead(404).end();return;}
  res.setHeader('Content-Type',name===script?'application/javascript':'text/css');
  res.end(fs.readFileSync(path.join(assets,name)));
});
await new Promise(resolve => server.listen(0,'127.0.0.1',resolve));
try {
  for(const [mode,size,zoom] of [['desktop','1600,1000',1],['wide','1920,1080',1],['narrow','720,900',1.25]]) {
    const profile=fs.mkdtempSync('D:/tmp/vm-nav-browser-');
    const browser=spawn(path.join(process.env['ProgramFiles(x86)'],'Microsoft/Edge/Application/msedge.exe'),
      ['--headless','--disable-gpu','--no-first-run','--no-default-browser-check','--disable-extensions','--disable-background-networking',
       '--hide-scrollbars', '--virtual-time-budget=6000', '--window-size='+size,'--user-data-dir='+profile,
       '--screenshot='+path.join(output,mode+'.png'),
       'http://127.0.0.1:'+server.address().port+'/?mode='+mode+'&zoom='+zoom],{windowsHide:true,stdio:'ignore',env:{...process.env,TEMP:profile,TMP:profile}});
    await new Promise((resolve,reject)=>{
      const timer=setTimeout(()=>{browser.kill();reject(Error('Preview timeout'));},45000);
      browser.once('error',error=>{clearTimeout(timer);reject(error);});
      browser.once('exit',code=>{clearTimeout(timer);code===0?resolve():reject(Error('Preview exit '+code));});
    });
    const result=metrics.get(mode); fs.writeFileSync(path.join(output,mode+'.json'),JSON.stringify(result??{error:'No metrics'},null,2));
    if(!result||result.error)throw Error(mode+': '+JSON.stringify(result));
    console.log(JSON.stringify(result));
  }
} finally { server.close(); }
