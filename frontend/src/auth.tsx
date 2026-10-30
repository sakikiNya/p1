import {useState, type FormEvent} from 'react';
import {api, type User, type Role} from './api';

export const roleNames: Record<Role,string> = {super_admin:'超级管理员',admin:'管理员',user:'普通用户'};
const errorMessage = (error:unknown) => error instanceof Error ? error.message : '请求失败，请重试';

export function Login({onLogin}:{onLogin:(user:User)=>void}) {
  const [username,setUsername] = useState('');
  const [password,setPassword] = useState('');
  const [busy,setBusy] = useState(false);
  const [error,setError] = useState('');
  async function submit(event:FormEvent) {
    event.preventDefault();if(busy)return;setBusy(true);setError('');
    try {onLogin(await api<User>('/auth/login',{method:'POST',body:JSON.stringify({username,password})}));}
    catch(error){setError(errorMessage(error));}
    finally{setBusy(false);}
  }
  return <main className="login-layout"><section className="login-intro"><p className="eyebrow">PARK INSPECTION</p><h1>现场发现，协作解决。</h1><p>登录园区巡检管理台，查看现场问题并跟进处理。</p></section><form className="account-form login-form" onSubmit={submit}><h2>登录管理台</h2><label>用户名<input autoComplete="username" value={username} onChange={e=>setUsername(e.target.value)} required disabled={busy}/></label><label>密码<input type="password" autoComplete="current-password" value={password} onChange={e=>setPassword(e.target.value)} required disabled={busy}/></label>{error&&<p className="error" role="alert">{error}</p>}<button disabled={busy}>{busy?'登录中…':'登录'}</button></form></main>;
}

export function Users() {
  const [users,setUsers] = useState<User[]>([]);
  const [loaded,setLoaded] = useState(false);
  const [username,setUsername] = useState('');
  const [password,setPassword] = useState('');
  const [role,setRole] = useState<'user'|'admin'>('user');
  const [busy,setBusy] = useState(false);
  const [error,setError] = useState('');
  const [success,setSuccess] = useState('');
  async function load(){setBusy(true);setError('');try{setUsers(await api<User[]>('/users'));setLoaded(true);}catch(error){setError(errorMessage(error));}finally{setBusy(false);}}
  async function add(event:FormEvent){
    event.preventDefault();if(busy)return;setBusy(true);setError('');setSuccess('');
    try{const user=await api<User>('/users',{method:'POST',body:JSON.stringify({username,password,role})});setUsers(list=>[...list,user]);setUsername('');setPassword('');setSuccess(`已添加用户 ${user.username}`);}
    catch(error){setError(errorMessage(error));}finally{setBusy(false);}
  }
  return <section className="users-section"><div className="section-title"><h2>用户管理</h2><button className="refresh" onClick={load} disabled={busy}>{loaded?'刷新用户':'加载用户列表'}</button></div><p className="muted">超级管理员可以添加普通用户和管理员。普通用户可查看问题，管理员可在页面更新处理状态。</p><form className="account-form user-form" onSubmit={add}><label>新用户名<input autoComplete="off" value={username} onChange={e=>setUsername(e.target.value)} pattern="[A-Za-z][A-Za-z0-9_.\-]{2,31}" title="3～32 位，以英文字母开头" required disabled={busy}/></label><label>初始密码<input type="password" autoComplete="new-password" value={password} onChange={e=>setPassword(e.target.value)} minLength={12} maxLength={72} required disabled={busy}/></label><label>账号角色<select value={role} onChange={e=>setRole(e.target.value as 'user'|'admin')} disabled={busy}><option value="user">普通用户</option><option value="admin">管理员</option></select></label><button disabled={busy}>添加用户</button></form>{error&&<p className="error" role="alert">{error}</p>}{success&&<p className="success" role="status">{success}</p>}{loaded&&<div className="table-wrap"><table><thead><tr><th>用户名</th><th>角色</th><th>创建时间</th></tr></thead><tbody>{users.map(user=><tr key={user.id}><td>{user.username}</td><td>{roleNames[user.role]}</td><td>{new Date(user.createdAt).toLocaleString('zh-CN')}</td></tr>)}</tbody></table></div>}</section>;
}

export function PasswordForm({onChanged}:{onChanged:()=>void}) {
  const [currentPassword,setCurrent] = useState('');
  const [newPassword,setNew] = useState('');
  const [busy,setBusy] = useState(false);
  const [error,setError] = useState('');
  async function submit(event:FormEvent){event.preventDefault();if(busy)return;setBusy(true);setError('');try{await api('/auth/password',{method:'PATCH',body:JSON.stringify({currentPassword,newPassword})});onChanged();}catch(error){setError(errorMessage(error));}finally{setBusy(false);}}
  return <section className="password-section"><h2>修改密码</h2><p className="muted">密码为 12～72 字节。保存后所有设备需重新登录。</p><form className="account-form user-form" onSubmit={submit}><label>当前密码<input type="password" autoComplete="current-password" value={currentPassword} onChange={e=>setCurrent(e.target.value)} required disabled={busy}/></label><label>新密码<input type="password" autoComplete="new-password" value={newPassword} onChange={e=>setNew(e.target.value)} required minLength={12} maxLength={72} disabled={busy}/></label><button disabled={busy}>{busy?'保存中…':'保存新密码'}</button></form>{error&&<p className="error" role="alert">{error}</p>}</section>;
}
