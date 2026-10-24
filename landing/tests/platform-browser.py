"""Exercise the real built landing page; screenshots are browser evidence only."""
import json,os,re,sys
from pathlib import Path
from playwright.sync_api import sync_playwright, expect
base=sys.argv[1] if len(sys.argv)>1 else 'http://127.0.0.1:3000'
output=Path(os.environ.get('BROWSER_OUTPUT','test-output/platform-browser'));output.mkdir(parents=True,exist_ok=True)
mac='Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 Version/18.0 Safari/605.1.15'
windows='Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/135.0.0.0 Safari/537.36'
cases=[('mac',mac,'MacIntel',0,'macos','en-US',1440,None),('windows',windows,'Win32',0,'windows','ru-RU',1440,None),('mac-arm',mac,'MacIntel',0,'macos','es-ES',1440,'arm'),('iphone','Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) Mobile','iPhone',5,'windows','en-US',390,None),('ipad',mac,'MacIntel',5,'windows','en-US',820,None),('android','Mozilla/5.0 (Linux; Android 15) AppleWebKit/537.36 Mobile','Linux armv8l',5,'windows','es-ES',360,None)]
with sync_playwright() as p:
 browser=p.chromium.launch(executable_path=os.environ.get("CHROMIUM_EXECUTABLE"))
 try:
  for name,ua,platform,touch,expected,locale,width,arch in cases:
   context=browser.new_context(user_agent=ua,locale=locale,viewport={'width':width,'height':1000},device_scale_factor=1)
   context.add_init_script('''Object.defineProperty(navigator,'platform',{value:%s}); Object.defineProperty(navigator,'maxTouchPoints',{value:%s}); Object.defineProperty(navigator,'userAgentData',{value:%s});'''%(json.dumps(platform),touch,('{platform:"macOS", getHighEntropyValues:async()=>({architecture:'+json.dumps(arch)+'})}' if arch else 'undefined')))
   # Verify the saved same-OS fallback, including where GitHub image delivery is unavailable.
   context.route('https://github.com/**',lambda route:route.abort())
   page=context.new_page();errors=[];page.on('pageerror',lambda error:errors.append(str(error)))
   page.goto(base,wait_until='networkidle')
   expect(page.locator('.platform-switcher button[aria-pressed=true]')).to_have_text(('macOS' if expected=='macos' else 'Windows')+('Your system' if name=='mac' else 'Ваша система' if name=='windows' else 'Tu sistema' if name=='mac-arm' else ''))
   expect(page.locator('.preview-image img')).to_have_attribute('data-platform',expected)
   image=page.locator('.preview-image img');expect(image).to_have_js_property('complete',True);assert image.evaluate('(i)=>i.naturalWidth')>700
   assert ('/macos/' in image.get_attribute('src'))==(expected=='macos')
   gallery=page.locator('.preview-tabs [role=tab]')
   expect(gallery).to_have_count(9 if expected=='windows' else 8)
   expect(gallery.first).to_have_text({'ru':'Актуальное','en':'Featured','es':'Destacados'}[locale.split('-')[0]])
   for index in range(gallery.count()):
    gallery.nth(index).click()
    expect(gallery.nth(index)).to_have_attribute('aria-selected','true')
    page.wait_for_function("() => { const image = document.querySelector('.preview-image img'); return image?.complete && image.naturalWidth > 700; }")
    current=page.locator('.preview-image img')
    expect(current).to_have_js_property('complete',True)
    assert current.evaluate('(i)=>i.naturalWidth')>700,f'{name}: unavailable gallery frame {index}'
    assert ('/macos/' in current.get_attribute('src'))==(expected=='macos')
   gallery.first.click()
   primary=page.locator('.hero-actions .platform-download')
   if expected=='macos':
    expect(primary.locator('.button-alternative')).to_have_attribute('href','/download?format=installer')
    assert page.locator('.installation').inner_text().find('SmartScreen')==-1
    if arch:expect(primary.locator('a.button-download')).to_have_attribute('href','/download?format=mac-arm64')
    else:
     picker=primary.locator('details');picker.locator('summary').click();expect(picker).to_have_attribute('open','')
     expect(picker.locator('a[href="/download?format=mac-arm64"]')).to_be_visible();expect(picker.locator('a[href="/download?format=mac-x64"]')).to_be_visible()
     picker.locator('summary').click();assert picker.get_attribute('open') is None
   else:
    expect(primary.locator('a.button-download')).to_have_attribute('href','/download?format=installer')
   # OS override changes downloads, installation and gallery together.
   if expected=='windows':
    page.locator('#preview-tab-network').click()
    expect(page.locator('.preview-image img')).to_have_attribute('src',re.compile('settings-lan-dark'))
   other='windows' if expected=='macos' else 'macos'
   page.locator('.platform-switcher button').nth(0 if other=='windows' else 1).click()
   expect(page.locator('.preview-image img')).to_have_attribute('data-platform',other)
   expect(page.locator('.hero-actions .platform-download')).to_have_attribute('data-platform',other)
   expect(page.locator('.preview-tabs [role=tab]')).to_have_count(9 if other=='windows' else 8)
   page.wait_for_function("() => { const image = document.querySelector('.preview-image img'); return image?.complete && image.naturalWidth > 700; }")
   expect(page.locator('.preview-image img')).to_have_js_property('complete',True)
   page.locator('.platform-switcher button').nth(0 if expected=='windows' else 1).click()
   tabs=page.locator('.preview-tabs [role=tab]');tabs.first.focus();page.keyboard.press('ArrowRight');expect(tabs.nth(1)).to_have_attribute('aria-selected','true')
   assert page.evaluate('document.documentElement.scrollWidth<=innerWidth+1'),f'{name}: horizontal page overflow'
   # Language override must survive OS switches and a reload.
   page.locator('.hero-language select').select_option('es');expect(page.locator('html')).to_have_attribute('lang','es')
   page.locator('.platform-switcher button').nth(1).click();expect(page.locator('html')).to_have_attribute('lang','es')
   page.reload(wait_until='networkidle');expect(page.locator('html')).to_have_attribute('lang','es')
   page.locator('.hero-language select').select_option('auto')
   page.screenshot(path=str(output/(name+'.png')),full_page=True)
   assert not errors,f'{name}: runtime errors: {errors}'
   context.close();print('PASS',name,flush=True)
  request=browser.new_context().request
  for kind,filename in [('installer','Kachalka-Setup-'),('mac-arm64','Kachalka-macos-arm64.dmg'),('mac-x64','Kachalka-macos-x64.dmg')]:
   response=request.get(base+'/download?format='+kind,max_redirects=0);assert response.status==302,(kind,response.status)
   assert response.headers['location'].startswith('https://github.com/peregond/ka4alka/releases/download/') and filename in response.headers['location']
   assert 'no-store' in response.headers['cache-control']
  choice=request.get(base+'/download?format=mac');assert choice.status==200 and 'Kachalka-macos-arm64.dmg' in choice.text() and 'Kachalka-macos-x64.dmg' in choice.text()
  print('PASS explicit package redirects and Mac architecture chooser',flush=True)
 finally:browser.close()
