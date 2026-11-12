import { Component, type ReactNode } from 'react'
export default class ErrorBoundary extends Component<{children:ReactNode},{failed:boolean}> {
  state={failed:false}
  static getDerivedStateFromError(){return {failed:true}}
  render(){
    if(this.state.failed)return <main className="flex min-h-screen items-center justify-center bg-canvas p-6"><div className="panel max-w-lg p-8"><h1 className="text-xl font-bold text-ink">Chưa thể hiển thị màn hình này</h1><p className="my-4 text-sm leading-6 text-slate-500">Bạn có thể tải lại ứng dụng. Nếu đang thực hiện giao dịch tiền hoặc kho, hãy tra cứu lịch sử trước khi tạo giao dịch mới.</p><button className="btn-primary" onClick={()=>{window.location.href='/'}}>Tải lại ứng dụng</button></div></main>
    return this.props.children
  }
}
