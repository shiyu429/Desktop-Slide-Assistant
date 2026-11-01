'use strict';
const english = {
  skip:'Skip to content',brand:'Desktop & Slide Assistant',navFeatures:'Features & uses',hero1:'Switch desktops.',hero2:'Present with ease.',
  lead:'Keep everyday actions within reach. Two little arrows for focused work and a smoother presentation.',download:'Download for Windows',try:'Try it below ↓',requirements:'Portable · Windows 10 / 11 · .NET Framework 4.8',
  demoLabel:'A HANDS-ON PREVIEW',desktop:'Desktop switching',slides:'Slide navigation',demoHint:'Try the arrows',demoDisclaimer:'An interactive illustration. It does not control your desktop or slides.',
  ribbon1:'Two useful features. One small widget.',ribbon2:'Made for mice, touchscreens & whiteboards',featureTitle:'The right action, in the right place.',desktopTitle:'A desktop for every part of your day.',
  desktopBody:'Move left and right between adjacent Windows virtual desktops. At the last desktop, click right to create and enter a new one. Left at the first desktop stays put.',fits:'GOOD FOR',desktopUse:'Lesson prep & teaching · Multitasking · Work & personal spaces',
  slideTitle:'Keep your presentation moving.',slideBody:'Appears only during a foreground, full-screen PowerPoint or WPS slideshow. The arrows follow the app’s native slide and animation steps. It hides when the show ends or another app takes the foreground.',slideUse:'Whiteboard lessons · Touch displays · Meetings · Workshops',
  detailsTitle:'A small presence. A useful hand.',dragTitle:'Place it where it feels right.',dragBody:'Drag freely and keep your position. Optional edge hiding expands on hover. Off by default for slide navigation.',immersiveTitle:'Less background. Same control.',immersiveBody:'Immersive appearance shows only black arrows and a divider, keeping the full button hit areas.',guideTitle:'A little guidance to get started.',guideBody:'Choose a feature on first launch. A spotlight points to the slide controls. Dismiss reminders for good, and decide whether to start at login.',
  startTitle:'Extract. Choose. Go.',step1:'Download & extract',step1Body:'Keep the EXE and its configuration file together. No installation needed.',step2:'Choose your feature',step2Body:'Desktop switching or slide navigation. Change it later in the widget or tray menu.',step3:'Make it your own',step3Body:'Drag it to a comfortable spot. Between slideshows, find the app in the system tray.',
  downloadTitle:'One click to what’s next.',downloadBody:'A portable little helper for Windows. Ready to make everyday actions feel easier.',viewSource:'View the source on GitHub ↗',
  compat:'Compatibility: slideshow recognition depends on PowerPoint / WPS window characteristics and may vary by version. Automatic desktop creation stops when desktop state cannot be reliably identified. The application UI is Chinese; this project website is bilingual.',footer:'Two arrows. Just enough.',feedback:'Report an issue ↗'
};
const chinese = new Map([...document.querySelectorAll('[data-i18n]')].map(node=>[node.dataset.i18n,node.innerHTML]));
let language='zh';
try { language=localStorage.getItem('desktop-switcher-language') || (navigator.language.toLowerCase().startsWith('zh')?'zh':'en'); } catch { /* Storage is optional. */ }
if(language!=='en') language='zh';
let mode='desktop',desktopIndex=0,desktopCount=3,slideIndex=0;
const byId=id=>document.getElementById(id);
const slideText={zh:['让想法\n清晰可见。','把重点\n娓娓道来。','每一步\n都有回应。','谢谢聆听。'],en:['Make ideas\nvisible.','Give every point\nits moment.','Keep everyone\nwith you.','Thank you.']};
function renderDemo(){
  const slides=mode==='slides',en=language==='en';
  byId('desktop-tab').setAttribute('aria-pressed',String(!slides));
  byId('slides-tab').setAttribute('aria-pressed',String(slides));
  byId('desktop-scene').hidden=slides;byId('slide-scene').hidden=!slides;
  const page=String((slides?slideIndex:desktopIndex)+1).padStart(2,'0');
  byId('screen-title').textContent=(slides?'SLIDE ':'DESKTOP ')+page;
  byId('scene-number').textContent=page;
  byId('window-tag').textContent='WORKSPACE / '+page;
  byId('slide-heading').textContent=slideText[language][slideIndex];
  byId('slide-count').textContent=page+' / 04';
  byId('slide-heading').style.whiteSpace='pre-line';
  const colors=['#c8dcb1','#c7d8e8','#e6d6b2'];
  byId('demo-screen').style.backgroundColor=slides?'#f5f0db':colors[desktopIndex%colors.length];
  byId('prev').disabled=(slides?slideIndex:desktopIndex)===0;
  byId('next').disabled=slides&&slideIndex===3;
  byId('prev').setAttribute('aria-label',en?(slides?'Previous slide':'Previous desktop'):(slides?'上一页':'上一个桌面'));
  byId('next').setAttribute('aria-label',en?(slides?'Next slide':'Next desktop'):(slides?'下一页':'下一个桌面'));
  byId('demo-status').textContent=slides?(en?`Slide ${slideIndex+1} of 4 · illustration`:`课件 ${slideIndex+1} / 4 · 网页示意`):(en?`Desktop ${desktopIndex+1} of ${desktopCount} · right at the end creates one`:`桌面 ${desktopIndex+1} / ${desktopCount} · 到末尾向右新建`);
}
function setLanguage(next){
  language=next;document.documentElement.lang=language==='en'?'en':'zh-CN';
  document.querySelectorAll('[data-i18n]').forEach(node=>{
    if(language==='en') node.textContent=english[node.dataset.i18n];
    else node.innerHTML=chinese.get(node.dataset.i18n);
  });
  byId('language').textContent=language==='en'?'中文':'EN';
  byId('language').setAttribute('aria-label',language==='en'?'切换到中文':'Switch to English');
  document.title=language==='en'?'Desktop & Slide Assistant':'桌面切屏/课件翻页助手 · DesktopSwitcher';
  try {localStorage.setItem('desktop-switcher-language',language);} catch { /* Still works without storage. */ }
  renderDemo();
}
byId('language').addEventListener('click',()=>setLanguage(language==='zh'?'en':'zh'));
byId('desktop-tab').addEventListener('click',()=>{mode='desktop';renderDemo();});
byId('slides-tab').addEventListener('click',()=>{mode='slides';renderDemo();});
byId('prev').addEventListener('click',()=>{if(mode==='slides')slideIndex=Math.max(0,slideIndex-1);else desktopIndex=Math.max(0,desktopIndex-1);renderDemo();});
byId('next').addEventListener('click',()=>{if(mode==='slides')slideIndex=Math.min(3,slideIndex+1);else{desktopIndex++;desktopCount=Math.max(desktopCount,desktopIndex+1);}renderDemo();});
setLanguage(language);
