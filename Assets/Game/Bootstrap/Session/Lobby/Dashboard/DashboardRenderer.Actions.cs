using System.Text;

namespace RaidDemo.Bootstrap
{
    /// <summary>
    /// 状态页的管理交互部分：口令输入、运维按钮与配套的少量脚本。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么从渲染主文件里拆出来：</b>主文件负责"把快照画成只读页面"，
    /// 这一半负责"让浏览器发起写操作"。两者改动原因不同（前者跟数据契约，后者跟交互），
    /// 而且单个源码文件有 400 行上限，拆开也让渲染断言更容易定位。</para>
    ///
    /// <para><b>为什么用一小段内联 JS 而不是表单：</b>管理与观看共用同一个 URL，
    /// 表单提交会带着口令出现在地址栏与浏览器历史里；这里把口令放进请求头，
    /// 它只存在于本页与这一个请求里。页面本来就要自动刷新，脚本已经不可避免。</para>
    /// </remarks>
    public static partial class DashboardRenderer
    {
        /// <summary>管理口令在浏览器里的存储键（localStorage）。</summary>
        private const string AdminTokenStorageKey = "raiddemo.adminToken";

        /// <summary>运维操作卡片。</summary>
        /// <param name="html">输出目标。</param>
        /// <param name="canControl">是否为服务器本机访问（免口令）。</param>
        /// <param name="adminEnabled">服务器是否配置了管理口令。</param>
        private static void AppendActionsCard(StringBuilder html, bool canControl, bool adminEnabled)
        {
            html.Append("<div class=\"card\"><h2>运维操作</h2>");

            if (!canControl && !adminEnabled)
            {
                html.Append("<div class=\"empty\">服务器未配置管理口令（配置项 adminToken）：");
                html.Append("写操作只接受来自服务器本机的请求。</div></div>");
                return;
            }

            html.Append(canControl
                ? "<div class=\"empty\">本机访问免口令。填入口令后也可以远程管理（口令保存在本浏览器）。</div>"
                : "<div class=\"empty\">远程管理需要口令：填入服务器配置的 adminToken。</div>");

            html.Append("<div style=\"margin:10px 0\">口令：");
            html.Append("<input id=\"adminToken\" type=\"password\" placeholder=\"adminToken\" autocomplete=\"off\"></div>");
            html.Append("<button class=\"btn\" onclick=\"act('/?action=stop-room', this, '确定解散房间吗？所有成员会被移出。')\">");
            html.Append("解散房间（回空闲）</button>");
            html.Append("<button class=\"btn\" onclick=\"act('/?action=stop-server', this, '确定停止服务器进程吗？')\">停止服务器</button>");
            html.Append("<div class=\"empty\">解散房间会把成员全部移出（不影响服务器进程）；停止服务器会写完日志后退出。</div>");
            html.Append("</div>");
        }

        /// <summary>页面脚本：口令记忆、操作请求与自动刷新。</summary>
        /// <param name="html">输出目标。</param>
        /// <param name="refreshSeconds">自动刷新间隔（秒）；0 表示不刷新。</param>
        /// <remarks>
        /// <para>自动刷新与口令输入框共存有一个坑：整页刷新会打断正在输入的焦点与内容。
        /// 因此刷新由脚本驱动，且输入框聚焦时跳过这一次——用户能安心把口令打完。</para>
        ///
        /// <para>口令存 localStorage 是刻意的取舍：这台机器上只有运维自己用浏览器，
        /// 存下来才不用每次操作重新输入。请求本身（fetch）只把它放进 <c>X-Admin-Token</c> 头。</para>
        /// </remarks>
        private static void AppendAdminScript(StringBuilder html, float refreshSeconds)
        {
            html.Append("<script>(function(){");
            html.Append($"var KEY='{AdminTokenStorageKey}';");
            html.Append("var input=document.getElementById('adminToken');");
            html.Append("if(input){try{var saved=localStorage.getItem(KEY);if(saved){input.value=saved;}}catch(e){}");
            html.Append("input.addEventListener('input',function(){try{localStorage.setItem(KEY,input.value.trim());}catch(e){}});}");
            html.Append("window.act=function(url,btn,confirmText){");
            html.Append("var text=confirmText||'';");
            html.Append("if(btn&&btn.dataset&&btn.dataset.nick){text='确定把「'+btn.dataset.nick+'」移出房间吗？';}");
            html.Append("if(text&&!confirm(text)){return;}");
            html.Append("var headers={};var box=document.getElementById('adminToken');");
            html.Append("if(box&&box.value){headers['X-Admin-Token']=box.value.trim();}");
            html.Append("fetch(url,{headers:headers}).then(function(r){return r.text();})");
            html.Append(".then(function(body){alert(body);location.reload();})");
            html.Append(".catch(function(e){alert('请求失败：'+e);});};");

            if (refreshSeconds > 0f)
            {
                html.Append($"var interval={refreshSeconds:F0}*1000;");
                html.Append("setInterval(function(){var box=document.getElementById('adminToken');");
                html.Append("if(!box||document.activeElement!==box){location.reload();}},interval);");
            }

            html.Append("})();</script>");
        }
    }
}
