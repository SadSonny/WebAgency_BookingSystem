// [INTENT]: Pagine HTML minimali servite dall'API per impostare/reimpostare la password (atterraggio dei link
// via email). Sono l'UNICA UI servita dal backend.
// EXCEPTION (AD-09): AD-09 vieta una Admin UI/dashboard nel backend; qui deroghiamo per le sole pagine tecniche
// di set-password, necessarie perché i link email devono atterrare da qualche parte senza obbligare ogni
// agenzia a re-implementarle. Nessuna gestione prenotazioni: solo un form che fa POST alla nostra API.

using System.Net;

namespace WebAgency_BookingSystem.Api.Http;

internal static class AccountHtmlPages
{
    /// <summary>Pagina con form per impostare la password; <paramref name="postPath"/> è l'endpoint POST target.</summary>
    public static string SetPasswordPage(string title, string token, string postPath)
    {
        string safeToken = WebUtility.HtmlEncode(token);
        return $$"""
        <!DOCTYPE html>
        <html lang="it"><head><meta charset="utf-8">
        <meta name="viewport" content="width=device-width,initial-scale=1">
        <title>{{WebUtility.HtmlEncode(title)}}</title>
        <style>body{font-family:Arial,sans-serif;background:#f4f4f5;margin:0;padding:40px}
        .card{max-width:420px;margin:auto;background:#fff;border:1px solid #e4e4e7;border-radius:8px;padding:24px}
        h1{font-size:20px;color:#111827}input{width:100%;padding:10px;margin:8px 0;border:1px solid #d1d5db;border-radius:6px;box-sizing:border-box}
        button{width:100%;padding:12px;background:#111827;color:#fff;border:0;border-radius:6px;font-size:15px;cursor:pointer}
        .msg{margin-top:12px;font-size:14px}</style></head>
        <body><div class="card"><h1>{{WebUtility.HtmlEncode(title)}}</h1>
        <form id="f"><input type="password" id="pwd" placeholder="Nuova password" minlength="12" required>
        <input type="password" id="pwd2" placeholder="Conferma password" required>
        <button type="submit">Conferma</button></form><div class="msg" id="m"></div></div>
        <script>
        const f=document.getElementById('f'),m=document.getElementById('m');
        f.onsubmit=async e=>{e.preventDefault();
          if(pwd.value!==pwd2.value){m.textContent='Le password non coincidono.';m.style.color='#b91c1c';return;}
          const r=await fetch('{{postPath}}',{method:'POST',headers:{'Content-Type':'application/json'},
            body:JSON.stringify({token:'{{safeToken}}',newPassword:pwd.value})});
          if(r.ok){m.style.color='#15803d';m.textContent='Fatto! Ora puoi accedere dal sito della tua attività.';f.style.display='none';}
          else{const j=await r.json().catch(()=>null);m.style.color='#b91c1c';m.textContent=(j&&j.message)||'Errore. Il link potrebbe essere scaduto.';}
        };
        </script></body></html>
        """;
    }
}
