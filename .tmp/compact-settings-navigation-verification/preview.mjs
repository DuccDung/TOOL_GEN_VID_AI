import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { spawn } from 'node:child_process';

const output = path.resolve('.tmp/compact-settings-navigation-verification');
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
  if(${JSON.stringify(mode)}==='compact') localStorage.setItem('videomaker.vietsub.editor-layout.project', JSON.stringify({settingsWidth:220,inspectorWidth:420,timelineHeight:250}));
  const wait = ms => new Promise(resolve => setTimeout(resolve, ms));
  const until = async predicate => { for(let i=0;i<80;i++){if(predicate())return;await wait(50);}throw Error('UI did not become ready'); };
  async function inspect(){
    try {
      await until(() => [...document.querySelectorAll('.sidebar-nav button')].some(button => button.textContent.trim()==='Dịch phụ đề'));
      [...document.querySelectorAll('.sidebar-nav button')].find(button => button.textContent.trim()==='Dịch phụ đề').click();
      await until(() => document.querySelector('.vietsub-new-project-button:not(:disabled)'));
      const button = document.querySelector('.vietsub-new-project-button');
      if(!button.getClientRects().length){
        [...document.querySelectorAll('.vietsub-editor-panel-tabs button')].find(tab=>tab.textContent.trim()==='Thiết lập').click();
        await until(()=>button.getClientRects().length>0);
      }
      const rect=button.getBoundingClientRect();
      const heading=button.closest('.vietsub-tools-heading');
      if(!heading||document.querySelector('.vietsub-project-navigation'))throw Error('Button not moved into settings');
      const header=heading.getBoundingClientRect();
      if(rect.left<header.left||rect.right>header.right||rect.top<header.top||rect.bottom>header.bottom)throw Error('Button outside settings header');
      if(header.height/${zoom}>82)throw Error('Settings header too tall');
      if(rect.left<0||rect.top<0||rect.right>innerWidth||rect.bottom>innerHeight) throw Error('Navigation button outside viewport');
      if(button.textContent.trim()!=='Tạo dự án mới') throw Error('Navigation label missing');
      const result={mode:${JSON.stringify(mode)},zoom:${zoom},button:{x:rect.x,y:rect.y,width:rect.width,height:rect.height},label:button.textContent.trim(),headerHeight:header.height/${zoom},insideSettings:true};
      if(${JSON.stringify(mode)}==='create') {
        button.click(); await until(() => document.querySelector('.vietsub-create-card input:not(:disabled)'));
        if(closes!==1||document.querySelector('.vietsub-editor-workspace'))throw Error('Navigation failed');
        result.creationForm=true; result.closeRequests=closes;
      }
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
  for(const [mode,size,zoom] of [['desktop','1600,1000',1],['compact','1600,1000',1],['narrow','720,900',1.25],['create','1200,900',1]].filter(([mode]) => process.argv.length < 3 || process.argv.slice(2).includes(mode))) {
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
