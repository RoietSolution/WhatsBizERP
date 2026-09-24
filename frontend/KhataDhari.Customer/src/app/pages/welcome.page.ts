import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  standalone: true,
  imports: [RouterLink],
  template: `
    <main class="welcome">
      <div class="welcome-top"><span class="brand-mark">K</span><strong>KhataDhari <span>shop</span></strong></div>
      <section class="welcome-card">
        <div class="eyebrow">YOUR NEIGHBOURHOOD, DELIVERED</div>
        <h1>Good things<br />are closer than<br /><em>you think.</em></h1>
        <p>Shop the stores you love, with the care of your neighbourhood and the ease of your phone.</p>
        <a routerLink="/guturgo" class="demo-link"><span>🛍️</span><span><b>Explore GuturGo</b><small>Development storefront preview</small></span><i>→</i></a>
      </section>
      <div class="welcome-foot">A little closer to home. <span>✳</span></div>
    </main>
  `,
  styles: [`
    :host { display:block; min-height:100vh; background:radial-gradient(ellipse at 80% 20%,#f7e9c5 0,transparent 38%),#f7f7f2; }
    .welcome { display:flex; flex-direction:column; min-height:100vh; width:min(1050px,100%); margin:auto; padding:26px 28px; }
    .welcome-top { display:flex; align-items:center; gap:11px; font:800 16px Manrope,sans-serif; }.welcome-top strong span { color:var(--green); font-weight:600; }
    .brand-mark { display:grid; width:39px;height:39px;place-items:center;border-radius:13px;color:#fff;background:var(--green);font:800 19px Manrope,sans-serif; }
    .welcome-card { max-width:530px; margin:auto 0; padding:60px 0 76px; }.eyebrow { color:var(--green);font-size:10px;letter-spacing:2px;font-weight:800; }
    h1 { margin:20px 0 15px; color:#1c3024; font:800 clamp(46px,9vw,76px)/.99 Manrope,sans-serif; letter-spacing:-4px; }h1 em { color:#c98c2b;font-style:normal; }
    p { max-width:380px;color:#747a70;font-size:15px;line-height:1.75; }
    .demo-link { display:flex;align-items:center;gap:13px;max-width:365px;margin-top:28px;padding:13px 15px;border:1px solid #e6e8dc;border-radius:16px;background:#fff;color:var(--ink);text-decoration:none;box-shadow:0 8px 26px #31553b0c; }
    .demo-link>span:first-child { display:grid;width:43px;height:43px;place-items:center;border-radius:13px;background:#f6f0df;font-size:22px; }.demo-link>span:nth-child(2){display:grid;gap:4px;flex:1}.demo-link b{font-size:13px}.demo-link small{color:var(--muted);font-size:10px}.demo-link i{color:var(--green);font-size:21px;font-style:normal}
    .welcome-foot { display:flex;justify-content:space-between;color:#8c9187;font-size:11px; }.welcome-foot span{color:#d59c33;font-size:20px}
    @media(max-width:600px){.welcome{padding:18px 20px}.welcome-card{padding:60px 0 70px}h1{letter-spacing:-2.8px}}
  `],
})
export class WelcomePage {}
