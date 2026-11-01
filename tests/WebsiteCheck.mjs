import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import {fileURLToPath} from 'node:url';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const html=fs.readFileSync(path.join(root,'docs/index.html'),'utf8');
const js=fs.readFileSync(path.join(root,'docs/app.js'),'utf8');
new vm.Script(js);
let checks=1;
for(const match of html.matchAll(/(?:href|src)="([^"]+)"/g)){
  const url=match[1]; if(url.startsWith('#')||url.startsWith('https://'))continue;
  if(!fs.existsSync(path.join(root,'docs',url)))throw new Error('Missing asset: '+url);checks++;
}
const keys=new Set([...html.matchAll(/data-i18n="([^"]+)"/g)].map(m=>m[1]));
const sandbox={}; vm.createContext(sandbox);
vm.runInContext(js.slice(0,js.indexOf('const chinese'))+'\nglobalThis.translations=english;',sandbox);
for(const key of keys){if(!sandbox.translations[key])throw new Error('Missing English translation: '+key);checks++;}
for(const file of ['README.md','README.en.md','docs/index.html','docs/app.js','docs/style.css']){
  const content=fs.readFileSync(path.join(root,file),'utf8');
  if(/C:\\Users\\|D:\\Users\\|7B52FE|diagnostics\.log/.test(content))throw new Error('Local diagnostic material in public file: '+file);checks++;
}
console.log(`PASS ${checks} website checks: JavaScript syntax, local assets, bilingual coverage, and public-file hygiene.`);
