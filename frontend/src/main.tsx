import React, {useEffect, useRef, useState} from 'react';
import {createRoot} from 'react-dom/client';
import {api, ApiError, isIssue, type Issue, type Status, type User} from './api';
import {Login,Users,PasswordForm,roleNames} from './auth';
import './style.css';

const statuses: Record<Status, string> = {open:'待处理', in_progress:'处理中', resolved:'已解决'};
const priorities = {low:'低',medium:'中',high:'高'};
const message = (e: unknown) => e instanceof Error ? e.message : '操作失败，请重试';
const time = (s: string) => new Date(s).toLocaleString('zh-CN', {hour12:false});
function IssueCard({issue,onSave,onBusy,canEdit}: {issue:Issue;onSave:(i:Issue)=>void;onBusy:(busy:boolean)=>void;canEdit:boolean}) {
  const [status,setStatus] = useState<Status>(issue.status);
  const [saving,setSaving] = useState(false);
  const [error,setError] = useState('');
  const [success,setSuccess] = useState('');
  useEffect(()=>{setStatus(issue.status)},[issue.status]);
  async function save() {
    if(saving) return;
    setSaving(true);onBusy(true);setError('');setSuccess('');
    try {
      const updated = await api<Issue>(`/issues/${encodeURIComponent(issue.id)}/status`,{method:'PATCH',body:JSON.stringify({status})});
      onSave(updated);setSuccess('状态已保存');
    } catch(e) {setStatus(issue.status);setError(message(e))}
    finally {setSaving(false);onBusy(false)}
  }
  return <article className="issue">
    <div className="issue-top"><span className={`priority ${issue.priority}`}>{priorities[issue.priority]}优先级</span><span className={`status ${issue.status}`}>{statuses[issue.status]}</span></div>
    <h2>{issue.title}</h2><p className="description">{issue.description || '暂无描述'}</p>
    <dl><div><dt>标记坐标 · 米</dt><dd>{['x','y','z'].map(k=>`${k.toUpperCase()} ${issue.position[k as 'x'|'y'|'z'].toFixed(3)}`).join(' / ')}</dd></div><div><dt>上报时间</dt><dd>{time(issue.createdAt)}</dd></div><div><dt>更新时间</dt><dd>{time(issue.updatedAt)}</dd></div></dl>
    <p className="id" title={issue.id}>编号 {issue.id}</p>
    <div className="actions"><label>处理状态<select aria-label={`${issue.title}的处理状态`} value={status} disabled={saving||!canEdit} onChange={e=>{setStatus(e.target.value as Status);setSuccess('')}}>{Object.entries(statuses).map(([key,label])=><option value={key} key={key}>{label}</option>)}</select></label>{canEdit&&<button onClick={save} disabled={saving||status===issue.status}>{saving?'保存中…':'保存状态'}</button>}</div>
    {error&&<p className="error" role="alert">{error}</p>}{success&&<p className="success" role="status">{success}</p>}
  </article>
}
function Issues({user}:{user:User}){
  const [issues,setIssues]=useState<Issue[]>([]),[loading,setLoading]=useState(true),[error,setError]=useState('');
  const [busy,setBusy]=useState(0),[loaded,setLoaded]=useState(false);
  const requesting=useRef(false);
  const [search,setSearch]=useState(''),[statusFilter,setStatusFilter]=useState(''),[priorityFilter,setPriorityFilter]=useState('');
  const visible=issues.filter(i=>(!statusFilter||i.status===statusFilter)&&(!priorityFilter||i.priority===priorityFilter)&&[i.title,i.description,i.id].some(s=>s.toLowerCase().includes(search.toLowerCase())));
  async function load(){
    if(requesting.current) return;requesting.current=true;setLoading(true);setError('');
    try{const data=await api<Issue[]>('/issues');if(!Array.isArray(data)||!data.every(isIssue))throw new Error('列表响应格式错误，请检查后端地址');setIssues(data);setLoaded(true)}
    catch(e){setError(message(e))}finally{requesting.current=false;setLoading(false)}
  }
  useEffect(()=>{void load()},[]);
  return <main><section className="intro"><div><p className="eyebrow">INSPECTION WORKSPACE</p><h1>让每个问题都有进展</h1><p>查看 AR 现场上报的问题，跟进处理状态。</p></div><button className="refresh" onClick={load} disabled={loading||busy>0}>{loading?'加载中…':'刷新列表'}</button></section>
  <section className="metrics" aria-label="问题概况">{(['open','in_progress','resolved'] as Status[]).map(s=><div key={s}><span>{statuses[s]}</span><strong>{loaded?issues.filter(i=>i.status===s).length:'—'}</strong></div>)}</section>
  <div className="filters"><label>搜索问题<input type="search" value={search} onChange={e=>setSearch(e.target.value)} placeholder="标题、描述或编号"/></label><label>状态<select value={statusFilter} onChange={e=>setStatusFilter(e.target.value)}><option value="">全部状态</option>{Object.entries(statuses).map(([value,label])=><option value={value} key={value}>{label}</option>)}</select></label><label>优先级<select value={priorityFilter} onChange={e=>setPriorityFilter(e.target.value)}><option value="">全部优先级</option>{Object.entries(priorities).map(([value,label])=><option value={value} key={value}>{label}</option>)}</select></label></div>
  <div className="section-title"><h2>问题记录</h2><span>{loaded?`共 ${issues.length} 条`: '等待加载'}</span></div>
  {error&&<div className="notice" role="alert"><strong>暂时无法完成请求</strong><p>{error}</p><button onClick={load} disabled={loading||busy>0}>重试</button>{loaded&&<p>下方保留上次加载的数据。</p>}</div>}
  {loading&&<p role="status" className="empty">正在获取问题…</p>}
  {!loading&&!error&&issues.length===0&&<div className="empty"><h2>还没有上报的问题</h2><p>在 Unity AR 端放置标记并提交后，刷新此处查看。</p></div>}
  <section className="grid">{visible.map(issue=><IssueCard key={issue.id} issue={issue} canEdit={user.role!=='user'} onSave={updated=>setIssues(list=>list.map(i=>i.id===updated.id?updated:i))} onBusy={v=>setBusy(n=>n+(v?1:-1))}/>)}</section>
  {!loading&&issues.length>0&&visible.length===0&&<p className="empty">没有符合筛选条件的问题。</p>}
  <footer>坐标属于上报时的 AR 会话，仅用于问题记录。</footer></main>;
}
function App(){
 const [user,setUser]=useState<User|null>(null),[loading,setLoading]=useState(true),[error,setError]=useState(''),[tab,setTab]=useState('issues'),[loggingOut,setLoggingOut]=useState(false);
 async function session(){setLoading(true);setError('');try{setUser(await api<User>('/auth/me'));}catch(e){if(!(e instanceof ApiError&&e.status===401))setError(message(e));}finally{setLoading(false);}}
 useEffect(()=>{void session()},[]);
 async function logout(){setLoggingOut(true);setError('');try{await api('/auth/logout',{method:'POST'});setUser(null);setTab('issues');}catch(e){setError(message(e));}finally{setLoggingOut(false);}}
 if(loading)return <main><p className="empty" role="status">正在检查登录状态…</p></main>;
 return <><header><div className="brand"><span className="brand-mark">巡</span><div>园区巡检<small>现场问题管理</small></div></div>{user&&<div className="session-controls"><span>{user.username} · {roleNames[user.role]}</span><button onClick={logout} disabled={loggingOut}>{loggingOut?'退出中…':'退出登录'}</button></div>}</header>
 {error&&<div className="notice" role="alert"><p>{error}</p><button onClick={session}>重试连接</button></div>}
 {!user?<Login onLogin={u=>{setUser(u);setError('');setTab('issues')}}/>:<><nav className="tabs" aria-label="管理页面"><button aria-current={tab==='issues'?'page':undefined} onClick={()=>setTab('issues')}>问题管理</button>{user.role==='super_admin'&&<button aria-current={tab==='users'?'page':undefined} onClick={()=>setTab('users')}>用户管理</button>}<button aria-current={tab==='password'?'page':undefined} onClick={()=>setTab('password')}>修改密码</button></nav>{tab==='issues'?<Issues user={user}/>:<main>{tab==='users'&&user.role==='super_admin'?<Users/>:<PasswordForm onChanged={()=>{setUser(null);setTab('issues')}}/>}</main>}</>}
 </>;
}
createRoot(document.getElementById('root')!).render(<React.StrictMode><App/></React.StrictMode>);
