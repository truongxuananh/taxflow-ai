import React from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import App from '../src/App'
import { AuthProvider } from '../src/auth/AuthContext'
import { api } from '../src/lib/api'
import assert from 'node:assert/strict'

const calls: any[] = []
let role='ADMIN'
const companies=[{id:'c1',name:'Công ty Minh Tâm',taxCode:'0402198765'},{id:'c2',name:'Chi nhánh Hà Nội',taxCode:'0402198766'}]
const session=()=>({accessToken:'dom-check',tokenType:'Bearer',expiresIn:28800,user:{id:'u1',email:'demo@example.com',fullName:'Nguyễn Minh Anh',role},companies,defaultCompanyId:'c1'})
api.defaults.adapter=async config=>{
 calls.push({url:config.url,company:config.headers['X-Company-Id']})
 let data:any=[]
 switch(config.url){
  case '/auth/login':case '/auth/me':data=session();break
  case '/dashboard':data={period:{from:'2026-11-01',to:'2026-11-12',days:30},kpis:{revenue:68250000,expense:23480000,estimatedProfit:44770000,currentCash:281950000,receivables:12200000,payables:8500000,outputVat:6200000,inputVat:2134500},trends:[],topProducts:[],alerts:[],disclaimer:'Kiểm thử giao diện'};break
  case '/browse/bank':case '/browse/orders':data={items:[],total:0,page:1,pageSize:20};break
  case '/products':data=[{id:'p1',sku:'SP001',name:'Giấy in A4',unit:'Hộp',purchasePrice:45000,sellingPrice:65000,vatRate:10,stock:34,minimumStock:10,isActive:true,category:{id:'cat1',name:'Văn phòng phẩm'},stockStatus:'OK'}];break
  case '/categories':data=[{id:'cat1',name:'Văn phòng phẩm'}];break
  case '/companies':data=companies;break
  case '/cash-flow':data={openingBalance:1,cashInflow:2,cashOutflow:1,netCashFlow:1,closingBalance:2,daily:[]};break
  case '/reports/summary':data={period:{type:'MONTHLY',from:'2026-11-01',to:'2026-11-12'},revenue:1,expense:1,estimatedProfit:0,vat:{input:0,output:0,difference:0},cashFlow:{inflow:1,outflow:1,net:0},debt:{receivable:0,payable:0},inventoryValue:1,disclaimer:'Kiểm thử'};break
  case '/tax/summary':data={period:{year:2026,month:11},vat:{input:1,output:2,difference:1},records:[],extensions:[],status:'DRAFT'};break
  case '/reconciliation':data={generatedAt:'2026-11-12',summary:{ordersWithoutInvoices:0,unpaidInvoices:0,unmatchedPayments:0,duplicateTransactions:0,amountMismatches:0},issues:[]};break
 }
 return {data,status:200,statusText:'OK',headers:{},config}
}
const root=createRoot(document.getElementById('root')!)
const mount=()=>root.render(<BrowserRouter><AuthProvider><App/></AuthProvider></BrowserRouter>)
const wait=async(fn:()=>boolean,label:string)=>{for(let i=0;i<100;i++){if(fn())return;await new Promise(r=>setTimeout(r,30))}throw new Error('Timeout: '+label)}
const click=(el:any)=>{assert(el);el.dispatchEvent(new window.MouseEvent('click',{bubbles:true}))}
const button=(name:string)=>Array.from(document.querySelectorAll('button')).find(b=>b.textContent?.trim()===name)
const navigate=async(path:string)=>{window.history.pushState({},'',path);window.dispatchEvent(new window.PopStateEvent('popstate'));await wait(()=>!!document.querySelector('h1'),'heading '+path)}
(async()=>{
 mount();await wait(()=>document.body.textContent!.includes('Đăng nhập hệ thống'),'login')
 document.querySelector('form')!.dispatchEvent(new window.Event('submit',{bubbles:true,cancelable:true}))
 await wait(()=>document.querySelectorAll('.metric-card').length===8,'dashboard')
 assert(document.body.textContent?.includes('Xin chào, Nguyễn Minh Anh'))
 assert.equal(document.querySelectorAll('.nav-item').length,15)
 await navigate('/products');await wait(()=>!!button('Thêm sản phẩm'),'products')
 click(button('Thêm sản phẩm'));await wait(()=>!!document.querySelector('[role="dialog"]'),'modal')
 assert.equal(document.body.style.overflow,'hidden')
 const dialog=document.querySelector('[role="dialog"]')!
 assert.equal(dialog.getAttribute('aria-labelledby'),dialog.querySelector('h2')!.id)
 await new Promise(r=>setTimeout(r,40))
 assert(dialog.contains(document.activeElement))
 document.dispatchEvent(new window.KeyboardEvent('keydown',{key:'Escape',bubbles:true}));await wait(()=>!document.querySelector('[role="dialog"]'),'Escape close')
 assert.notEqual(document.body.style.overflow,'hidden')
 const select=document.querySelector('header select') as HTMLSelectElement;select.value='c2';select.dispatchEvent(new window.Event('change',{bubbles:true}))
 await wait(()=>calls.some(c=>c.url==='/products'&&c.company==='c2'),'company change refetch')
 click(document.querySelector('[aria-label="Mở menu"]'));await wait(()=>!!document.querySelector('[aria-label="Menu điều hướng"]'),'drawer')
 assert.equal(document.querySelector('[aria-label="Mở menu"]')!.getAttribute('aria-expanded'),'true')
 document.dispatchEvent(new window.KeyboardEvent('keydown',{key:'Escape',bubbles:true}));await wait(()=>!document.querySelector('[aria-label="Menu điều hướng"]'),'drawer close')
 const paths=['/account','/records','/inventory','/invoices','/finance','/debts','/reconciliation','/tax','/reports','/assistant','/alerts','/company','/pos']
 for(const path of paths){await navigate(path);await new Promise(r=>setTimeout(r,90));await wait(()=>!!document.querySelector('h1'),'page heading '+path)}
 role='EMPLOYEE';click(document.querySelector('[aria-label="Đăng xuất"]'));await wait(()=>!!document.querySelector('form'),'logout');document.querySelector('form')!.dispatchEvent(new window.Event('submit',{bubbles:true,cancelable:true}));await wait(()=>document.querySelectorAll('.nav-item').length===5,'employee nav')
 assert.equal(document.querySelectorAll('a[href="/assistant"]').length,0)
 await navigate('/finance');await wait(()=>window.location.pathname==='/','role redirect')
 root.unmount()
 console.log(JSON.stringify({status:'PASS',routes:15,checks:['login/logout','8 animated metric cards','role-specific navigation','modal title and initial focus','Escape and scroll lock','company change refetch','mobile menu open/close','protected route redirect'],note:'DOM tests with mocked API responses; no live backend or visual browser verification'},null,2))
})().catch(err=>{console.error(err);process.exitCode=1;root.unmount()})
