export type Status = 'open' | 'in_progress' | 'resolved';
export type Issue = {
  id: string; title: string; description: string;
  priority: 'low' | 'medium' | 'high'; status: Status;
  position: {x: number; y: number; z: number}; createdAt: string; updatedAt: string;
};
const base = (import.meta.env.VITE_API_BASE_URL || '/api').replace(/\/$/, '');
export async function api<T>(path: string, init?: RequestInit): Promise<T> {
  try {
    const response = await fetch(base + path, {...init, signal: AbortSignal.timeout(15000), headers: {'Content-Type': 'application/json', ...init?.headers}});
    const data = await response.json().catch(() => null);
    if (!response.ok) throw new Error(data?.error?.message || `服务器返回错误（${response.status}）`);
    if (data === null) throw new Error('服务器返回了无效数据，请检查 API 地址');
    return data as T;
  } catch (error) {
    if (error instanceof TypeError || (error instanceof DOMException && ['TimeoutError', 'AbortError'].includes(error.name))) {
      throw new Error('无法连接后端或请求超时，请确认服务运行后重试');
    }
    throw error;
  }
}
