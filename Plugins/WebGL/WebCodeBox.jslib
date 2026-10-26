// WebGL（UnityRoom など）で、セーブコードのコピー・貼り付けをするための、ブラウザ側の入力欄。
// Unity の入力欄では、ブラウザのクリップボードを使えないため、HTML の textarea を、ゲームの上に出す。
mergeInto(LibraryManager.library, {

    WebCodeBox_Show: function (titlePtr, textPtr, readOnly, goPtr, submitPtr, closePtr, noticePtr, submitLabelPtr) {
        var title = UTF8ToString(titlePtr);
        var text = UTF8ToString(textPtr);
        var go = UTF8ToString(goPtr);
        var submitM = UTF8ToString(submitPtr);
        var closeM = UTF8ToString(closePtr);
        var notice = UTF8ToString(noticePtr);
        var submitLabel = UTF8ToString(submitLabelPtr);
        var ro = readOnly != 0;

        var send = function (method, value) {
            try {
                if (typeof SendMessage === 'function') SendMessage(go, method, value);
                else if (typeof Module !== 'undefined' && Module.SendMessage) Module.SendMessage(go, method, value);
            } catch (e) { console.warn(e); }
        };

        var old = document.getElementById('webcodebox');
        if (old) old.remove();

        var wrap = document.createElement('div');
        wrap.id = 'webcodebox';
        wrap.style.cssText = 'position:fixed;left:0;top:0;width:100%;height:100%;background:rgba(0,0,0,0.78);z-index:2147483647;display:flex;align-items:center;justify-content:center;font-family:sans-serif;';

        var box = document.createElement('div');
        box.style.cssText = 'background:#1b1b25;color:#fff;padding:18px 20px;width:min(900px,92%);max-height:92%;border-radius:8px;box-shadow:0 0 24px #000;';

        var h = document.createElement('div');
        h.textContent = title;
        h.style.cssText = 'font-size:22px;font-weight:bold;margin-bottom:6px;';
        box.appendChild(h);

        if (notice) {
            var n = document.createElement('div');
            n.textContent = notice;
            n.style.cssText = 'font-size:14px;color:#ffcf9a;margin-bottom:8px;';
            box.appendChild(n);
        }

        var ta = document.createElement('textarea');
        ta.value = text;
        ta.readOnly = ro;
        ta.spellcheck = false;
        ta.placeholder = ro ? '' : 'ここに、セーブコードを貼り付けてください（Ctrl+V）';
        ta.style.cssText = 'width:100%;height:240px;box-sizing:border-box;background:#0f0f16;color:#e8e8f0;border:1px solid #555;border-radius:4px;padding:8px;font-size:13px;font-family:monospace;resize:vertical;';
        box.appendChild(ta);

        var msg = document.createElement('div');
        msg.style.cssText = 'font-size:14px;min-height:20px;margin:6px 0;color:#99ff99;';
        box.appendChild(msg);

        var row = document.createElement('div');
        row.style.cssText = 'display:flex;gap:10px;justify-content:flex-end;';

        var mkBtn = function (label, color) {
            var b = document.createElement('button');
            b.textContent = label;
            b.style.cssText = 'font-size:16px;padding:8px 18px;border:0;border-radius:4px;color:#fff;cursor:pointer;background:' + color + ';';
            return b;
        };

        var close = function () {
            wrap.remove();
            send(closeM, '');
        };

        if (ro) {
            var copyBtn = mkBtn('コピー', '#2f6f4f');
            copyBtn.onclick = function () {
                ta.focus();
                ta.select();
                var ok = false;
                try { ok = document.execCommand('copy'); } catch (e) { ok = false; }
                if (!ok && navigator.clipboard && navigator.clipboard.writeText) {
                    navigator.clipboard.writeText(ta.value).then(function () {
                        msg.style.color = '#99ff99'; msg.textContent = 'コピーしました。';
                    }, function () {
                        msg.style.color = '#ff9a8a'; msg.textContent = 'コピーできませんでした。文字を選んで、Ctrl+C を押してください。';
                    });
                    return;
                }
                msg.style.color = ok ? '#99ff99' : '#ff9a8a';
                msg.textContent = ok ? 'コピーしました。' : 'コピーできませんでした。文字を選んで、Ctrl+C を押してください。';
            };
            row.appendChild(copyBtn);
            var closeBtn = mkBtn('閉じる', '#555566');
            closeBtn.onclick = close;
            row.appendChild(closeBtn);
        } else {
            var okBtn = mkBtn(submitLabel || 'OK', '#8a3a3a');
            okBtn.onclick = function () {
                var v = ta.value;
                wrap.remove();
                send(submitM, v);
            };
            row.appendChild(okBtn);
            var cancelBtn = mkBtn('キャンセル', '#555566');
            cancelBtn.onclick = close;
            row.appendChild(cancelBtn);
        }
        box.appendChild(row);
        wrap.appendChild(box);

        // キーボードの入力が、ゲームに渡らないようにする
        ['keydown', 'keyup', 'keypress'].forEach(function (ev) {
            wrap.addEventListener(ev, function (e) { e.stopPropagation(); });
        });

        document.body.appendChild(wrap);
        ta.focus();
        if (ro) ta.select();
    },

    WebCodeBox_Hide: function () {
        var old = document.getElementById('webcodebox');
        if (old) old.remove();
    }
});
