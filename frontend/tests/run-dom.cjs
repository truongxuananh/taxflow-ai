const { JSDOM } = require('jsdom');
const esbuild = require('esbuild');
const path = require('node:path');
const fs = require('node:fs');
const os = require('node:os');
const out = path.join(fs.mkdtempSync(path.join(os.tmpdir(), 'taxflow-dom-')), 'check.cjs');
esbuild.buildSync({ entryPoints:[path.join(__dirname,'dom-check.tsx')], bundle:true, platform:'node', format:'cjs', outfile:out, jsx:'automatic',define:{'import.meta.env':'{"VITE_API_URL":"/api"}'},logLevel:'warning'});
const dom = new JSDOM('<!doctype html><html><body><div id="root"></div></body></html>', {url:'http://localhost:3000',pretendToBeVisual:true});
for (const key of ['Event','Element','window','document','localStorage','HTMLElement','HTMLInputElement','SVGElement','MutationObserver','getComputedStyle','requestAnimationFrame','cancelAnimationFrame']) global[key] = dom.window[key];
// Dùng chung đồng hồ của Node cho requestAnimationFrame để thư viện animation tính đúng thời gian.
global.requestAnimationFrame=dom.window.requestAnimationFrame=cb=>setTimeout(()=>cb(performance.now()),16);
global.cancelAnimationFrame=dom.window.cancelAnimationFrame=id=>clearTimeout(id);
Object.defineProperty(global,'navigator',{value:dom.window.navigator,configurable:true});
window.matchMedia=()=>({matches:true,addEventListener(){},removeEventListener(){}});
global.ResizeObserver=class { observe(){} unobserve(){} disconnect(){} };
window.HTMLElement.prototype.scrollIntoView=function(){};
window.scrollTo=function(){};
require(out);
process.on('exit',()=>{fs.rmSync(path.dirname(out),{recursive:true,force:true})});
