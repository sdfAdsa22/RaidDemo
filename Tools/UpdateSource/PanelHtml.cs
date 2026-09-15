namespace RaidDemo.UpdateSource
{
    /// <summary>
    /// 内嵌的管理面板（单页 HTML + 原生 JS）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么内嵌而不是放静态文件：</b>服务端要能在云主机上以一个文件运行
    /// （自包含单文件发布），任何外部静态资源都会变成部署时的额外步骤与出错点。
    /// 面板不依赖任何 CDN 与前端构建链，断网也能用。</para>
    ///
    /// <para><b>口令怎么用：</b>写操作（上传 / 发布 / 删除）需要口令，
    /// 面板把它存在浏览器 localStorage 并通过 <c>X-Auth-Token</c> 发送；
    /// 只读浏览与下载不需要口令——"别人能复现整条链路"是本项目刻意保留的性质。</para>
    /// </remarks>
    public static class PanelHtml
    {
        /// <summary>面板页面内容。</summary>
        public const string Content = @"<!DOCTYPE html>
<html lang=""zh-cn"">
<head>
<meta charset=""utf-8"" />
<meta name=""viewport"" content=""width=device-width, initial-scale=1"" />
<title>RaidDemo 更新源</title>
<style>
  :root { --bg:#14171c; --panel:#1c2029; --line:#2b3140; --text:#e8ebf2; --dim:#96a0b5;
          --accent:#ffb454; --ok:#5fd08a; --bad:#ff6b6b; }
  * { box-sizing:border-box; }
  body { margin:0; background:var(--bg); color:var(--text);
         font-family:""Microsoft YaHei UI"",""PingFang SC"",system-ui,sans-serif; font-size:14px; }
  header { padding:18px 24px; border-bottom:1px solid var(--line); display:flex;
           align-items:center; gap:16px; flex-wrap:wrap; }
  h1 { font-size:18px; margin:0; letter-spacing:1px; }
  .tag { background:var(--panel); border:1px solid var(--line); border-radius:999px;
         padding:3px 10px; color:var(--dim); font-size:12px; }
  main { padding:18px 24px 40px; display:grid; gap:18px; }
  section { background:var(--panel); border:1px solid var(--line); border-radius:12px; padding:16px 18px; }
  h2 { font-size:15px; margin:0 0 12px; color:var(--accent); font-weight:600; }
  table { width:100%; border-collapse:collapse; }
  th,td { text-align:left; padding:7px 8px; border-bottom:1px solid var(--line); }
  th { color:var(--dim); font-weight:500; font-size:12px; }
  tr.cur td { color:var(--ok); }
  button { background:var(--accent); color:#1a1d24; border:0; border-radius:8px;
           padding:6px 12px; cursor:pointer; font-weight:600; font-size:13px; }
  button.ghost { background:transparent; color:var(--text); border:1px solid var(--line); }
  button:disabled { opacity:.45; cursor:not-allowed; }
  input[type=text],input[type=password],input[type=file] { background:#12151a; color:var(--text);
           border:1px solid var(--line); border-radius:8px; padding:6px 10px; }
  .row { display:flex; gap:10px; align-items:center; flex-wrap:wrap; }
  .grid { display:grid; grid-template-columns:repeat(auto-fit,minmax(180px,1fr)); gap:12px; }
  .kv { background:#12151a; border:1px solid var(--line); border-radius:10px; padding:10px 12px; }
  .kv b { display:block; color:var(--dim); font-weight:500; font-size:12px; margin-bottom:4px; }
  progress { width:100%; height:10px; }
  .muted { color:var(--dim); }
  .bad { color:var(--bad); }
  .ok { color:var(--ok); }
  #toast { position:fixed; right:20px; bottom:20px; background:var(--panel);
           border:1px solid var(--line); border-left:4px solid var(--accent);
           padding:10px 14px; border-radius:8px; display:none; max-width:420px; }
</style>
</head>
<body>
<header>
  <h1>RaidDemo · 更新源</h1>
  <span class=""tag"" id=""tagRoot"">—</span>
  <span class=""tag"" id=""tagFree"">—</span>
  <span class=""tag"" id=""tagMode"">—</span>
  <span style=""flex:1""></span>
  <span class=""muted"">口令</span>
  <input type=""password"" id=""token"" placeholder=""写操作口令"" style=""width:180px"" />
  <button class=""ghost"" onclick=""saveToken()"">保存</button>
</header>

<main>
  <section>
    <h2>当前发布</h2>
    <div class=""grid"" id=""currentGrid""></div>
  </section>

  <section>
    <h2>版本历史</h2>
    <table>
      <thead><tr><th>版本</th><th>文件</th><th>大小</th><th>生成时间</th><th>层</th><th>操作</th></tr></thead>
      <tbody id=""versions""></tbody>
    </table>
    <p class=""muted"" id=""versionsEmpty"" style=""display:none"">还没有任何版本。上传一个版本包就能开始。</p>
  </section>

  <section>
    <h2>上传新版本</h2>
    <div class=""row"">
      <input type=""text"" id=""uploadVersion"" placeholder=""版本号（例 0.10.1）"" style=""width:180px"" />
      <input type=""file"" id=""uploadFile"" accept="".zip"" />
      <label class=""muted""><input type=""checkbox"" id=""uploadOverwrite"" /> 覆盖同名版本</label>
      <button onclick=""upload()"" id=""uploadButton"">上传</button>
    </div>
    <p class=""muted"">
      上传""生成更新清单""产出的版本目录压缩包（根目录含 manifest.json 与 body/）。
      服务端会先校验清单与逐文件哈希，**通过后才**放进版本目录——坏包不会污染版本历史。
    </p>
    <progress id=""uploadProgress"" value=""0"" max=""100"" style=""display:none""></progress>
  </section>

  <section>
    <h2>访问日志（最近 50 条）</h2>
    <table>
      <thead><tr><th>时间</th><th>方法</th><th>路径</th><th>状态</th><th>耗时</th><th>字节</th></tr></thead>
      <tbody id=""logs""></tbody>
    </table>
  </section>
</main>

<div id=""toast""></div>

<script>
const TOKEN_KEY = 'raiddemo.updateSource.token';

function $(id) { return document.getElementById(id); }

function toast(message, bad) {
  const box = $('toast');
  box.textContent = message;
  box.style.borderLeftColor = bad ? '#ff6b6b' : '#ffb454';
  box.style.display = 'block';
  clearTimeout(box._timer);
  box._timer = setTimeout(() => { box.style.display = 'none'; }, 6000);
}

function saveToken() {
  localStorage.setItem(TOKEN_KEY, $('token').value);
  toast('口令已保存在本机浏览器。');
}

function token() { return localStorage.getItem(TOKEN_KEY) || $('token').value || ''; }

function formatBytes(value) {
  if (value < 0) return '—';
  if (value >= 1073741824) return (value / 1073741824).toFixed(2) + ' GB';
  if (value >= 1048576) return (value / 1048576).toFixed(1) + ' MB';
  if (value >= 1024) return (value / 1024).toFixed(0) + ' KB';
  return value + ' B';
}

async function refresh() {
  try {
    const status = await (await fetch('/api/status')).json();
    $('tagRoot').textContent = status.root;
    $('tagFree').textContent = '剩余 ' + formatBytes(status.freeBytes);
    $('tagMode').textContent = status.readOnly ? '只读模式' : '可写';

    $('currentGrid').innerHTML = [
      ['本体版本', status.current.version || '（未发布）'],
      ['资源版本', status.current.contentVersion || '—'],
      ['代码版本', status.current.codeVersion || '—'],
      ['生成时间', status.current.generatedAt || '—'],
    ].map(([k, v]) => `<div class=""kv""><b>${k}</b>${v}</div>`).join('');

    const rows = status.versions.map(v => `
      <tr class=""${v.isCurrent ? 'cur' : ''}"">
        <td>${v.version}${v.isCurrent ? ' · 当前' : ''}</td>
        <td>${v.fileCount}</td>
        <td>${formatBytes(v.totalBytes)}</td>
        <td class=""muted"">${v.generatedAt || '—'}</td>
        <td class=""muted"">${v.hasContent ? '资源 ' : ''}${v.hasCode ? '代码' : ''}${(!v.hasContent && !v.hasCode) ? '仅本体' : ''}</td>
        <td>
          <button class=""ghost"" onclick=""verify('${v.version}')"">校验</button>
          <button onclick=""publish('${v.version}')"">${v.isCurrent ? '重新发布' : '发布'}</button>
          <button class=""ghost"" onclick=""remove('${v.version}')"" ${v.isCurrent ? 'disabled' : ''}>删除</button>
        </td>
      </tr>`).join('');

    $('versions').innerHTML = rows;
    $('versionsEmpty').style.display = rows ? 'none' : 'block';

    const logs = await (await fetch('/api/log')).json();
    $('logs').innerHTML = logs.map(l => `
      <tr><td class=""muted"">${l.time}</td><td>${l.method}</td><td>${l.path}</td>
      <td class=""${l.status >= 400 ? 'bad' : 'ok'}"">${l.status}</td>
      <td class=""muted"">${l.milliseconds} ms</td><td class=""muted"">${formatBytes(l.bytes)}</td></tr>`).join('');
  } catch (error) {
    toast('读取状态失败：' + error, true);
  }
}

async function post(url, body) {
  const options = { method: 'POST', headers: { 'X-Auth-Token': token() } };
  if (body) options.body = body;
  const response = await fetch(url, options);
  return await response.json();
}

async function publish(version) {
  if (!confirm('把 ' + version + ' 设为当前发布版本？所有客户端下一次检查都会更新到它。')) return;
  const result = await post('/api/publish?version=' + encodeURIComponent(version));
  toast(result.message, !result.success);
  refresh();
}

async function remove(version) {
  if (!confirm('删除版本 ' + version + '？该版本目录会被永久移除。')) return;
  const result = await post('/api/delete?version=' + encodeURIComponent(version));
  toast(result.message, !result.success);
  refresh();
}

async function verify(version) {
  toast('正在校验 ' + version + ' …');
  const report = await (await fetch('/api/verify?version=' + encodeURIComponent(version))).json();
  if (report.passed) {
    toast('校验通过：' + report.checkedFiles + ' 个文件全部与清单一致。');
  } else {
    toast('校验失败：' + report.problems.slice(0, 3).join('；'), true);
  }
}

function upload() {
  const file = $('uploadFile').files[0];
  const version = $('uploadVersion').value.trim();
  if (!file || !version) { toast('请填写版本号并选择 zip 文件。', true); return; }

  const query = '/api/upload?version=' + encodeURIComponent(version) +
                ($('uploadOverwrite').checked ? '&overwrite=true' : '');
  const request = new XMLHttpRequest();
  request.open('POST', query);
  request.setRequestHeader('X-Auth-Token', token());
  request.setRequestHeader('Content-Type', 'application/zip');

  $('uploadProgress').style.display = 'block';
  $('uploadButton').disabled = true;

  request.upload.onprogress = event => {
    if (event.lengthComputable) {
      $('uploadProgress').value = Math.round(event.loaded * 100 / event.total);
    }
  };

  request.onload = () => {
    $('uploadButton').disabled = false;
    let result = {};
    try { result = JSON.parse(request.responseText); } catch (error) { result = { success: false, message: request.responseText }; }
    toast(result.message || ('HTTP ' + request.status), !result.success);
    if (result.success) { $('uploadFile').value = ''; }
    refresh();
  };

  request.onerror = () => {
    $('uploadButton').disabled = false;
    toast('上传失败：网络错误。', true);
  };

  request.send(file);
}

$('token').value = localStorage.getItem(TOKEN_KEY) || '';
refresh();
setInterval(refresh, 5000);
</script>
</body>
</html>";
    }
}
