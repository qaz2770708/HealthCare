"""Black-box integration tests. Python standard library; all writes use a fresh temporary database."""
import datetime as dt
import http.cookiejar
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import urllib.request
import urllib.error

ROOT = Path(__file__).resolve().parents[1]
DLL = ROOT / 'bin' / 'Debug' / 'net10.0' / 'HealthCare.dll'
BASE = 'http://127.0.0.1:5081'
passed = []
def check(condition, name):
    assert condition, name
    passed.append(name)
    print('PASS ' + name, flush=True)

class Client:
    def __init__(self):
        self.jar = http.cookiejar.CookieJar()
        self.http = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(self.jar))
        self.token = ''
    def req(self, path, method='GET', data=None, expected=200, csrf=True):
        headers = {'Content-Type':'application/json'}
        if csrf: headers['X-CSRF-TOKEN'] = self.token
        req = urllib.request.Request(BASE + '/api' + path, data=json.dumps(data).encode() if data is not None else None, headers=headers, method=method)
        try:
            with self.http.open(req, timeout=15) as res: status, body = res.status, res.read()
        except urllib.error.HTTPError as ex: status, body = ex.code, ex.read()
        assert status == expected, f'{method} {path}: expected {expected}, got {status}: {body.decode()}'
        return json.loads(body) if body else None
    def refresh(self): self.token = self.req('/auth/token')['token']
    def login(self, username, password='ClinicDemo!2026'):
        self.refresh()
        result = self.req('/auth/login','POST',{'username':username,'password':password})
        self.refresh()
        return result

def start(data, log):
    env = os.environ.copy()
    env.update(ASPNETCORE_ENVIRONMENT='Development', Clinic__DataDirectory=str(data), Clinic__SeedDemo='true')
    process = subprocess.Popen(['dotnet',str(DLL),'--urls',BASE],cwd=ROOT,env=env,stdout=log,stderr=log)
    for _ in range(100):
        if process.poll() is not None: raise RuntimeError('Server failed to start; inspect integration-server.log')
        try:
            urllib.request.urlopen(BASE+'/health',timeout=.5).close()
            return process
        except (OSError,urllib.error.URLError): time.sleep(.15)
    process.terminate(); raise RuntimeError('Server readiness timeout')

def run():
    if not DLL.exists(): raise SystemExit('Run dotnet build before tests.')
    temp_root = Path(os.environ.get('TEST_WORK_DIR', tempfile.gettempdir()))
    temp_root.mkdir(parents=True,exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='qinghe-test-',dir=temp_root) as tmp:
        temp = Path(tmp)
        with open(temp_root/'integration-server.log','w',encoding='utf-8') as log:
            process = start(temp,log)
            try:
                anonymous=Client(); anonymous.req('/patients',expected=401)
                anonymous.req('/auth/login','POST',{'username':'admin','password':'ClinicDemo!2026'},expected=400,csrf=False)
                check(True,'Anonymous API access and missing CSRF token rejected')
                admin=Client(); admin.login('admin')
                doctor=Client(); doc=doctor.login('doctor')
                reception=Client(); reception.login('reception')
                other_session=Client(); other_session.login('admin')
                check(True,'Three roles authenticate with cookie sessions')
                config=admin.req('/auth/config'); today=dt.date.fromisoformat(config['today']); tomorrow=today+dt.timedelta(days=1)
                dashboard=admin.req('/dashboard')
                check(dashboard['patients']==12 and dashboard['total']==10,'Dashboard aggregates seeded clinical data')
                reception.req('/audit',expected=403); doctor.req('/invoices',expected=403); reception.req('/staff',expected=403)
                check(reception.req('/patients/1')['history']==[],'Reception cannot read medical history or administrative data')
                check(True,'Doctor cannot access billing')
                patient={'name':'测试患者','gender':'未知','birthDate':'1990-05-06','phone':'13912345678','address':'测试地址','allergies':'未提供'}
                doctor.req('/patients','POST',patient,expected=403)
                admin.req('/patients','POST',{**patient,'birthDate':str(tomorrow)},expected=400)
                admin.req('/patients','POST',{**patient,'phone':'invalid'},expected=400)
                p=reception.req('/patients','POST',patient)
                check(p['id']>12,'Create patient with server-side date and phone validation')
                updated=reception.req('/patients/'+str(p['id']),'PUT',{**patient,'name':'测试患者更新','version':p['version']})
                reception.req('/patients/'+str(p['id']),'PUT',{**patient,'version':p['version']},expected=409)
                check(updated['version']==2,'Patient optimistic concurrency rejects stale edits')
                doctors=admin.req('/doctors'); slots=admin.req(f"/slots?doctorId={doc['id']}&date={tomorrow}")
                slot=next(s['slot'] for s in slots if s['available'])
                booking={'patientId':p['id'],'doctorId':doc['id'],'slot':slot,'reason':'测试预约'}
                a=reception.req('/appointments','POST',booking)
                admin.req('/appointments','POST',booking,expected=409)
                admin.req('/appointments','POST',{**booking,'doctorId':next(d['id'] for d in doctors if d['id']!=doc['id'])},expected=409)
                check(True,'Duplicate doctor slot and simultaneous patient booking rejected')
                admin.req('/appointments','POST',{**booking,'slot':str(today-dt.timedelta(days=1))+'T10:00:00'},expected=400)
                admin.req('/appointments','POST',{**booking,'slot':str(tomorrow)+'T10:15:00'},expected=400)
                admin.req(f"/appointments/{a['id']}/checkin",'POST',{'version':1},expected=409)
                check(True,'Past, nonstandard and premature check-in requests rejected')
                admin.req(f"/appointments/{a['id']}/cancel",'POST',{'version':1})
                a=admin.req('/appointments','POST',booking)
                check(a['id']>0,'Cancellation releases appointment slot')
                own=doctor.req(f'/appointments?date={today}')['items']
                check(all(a['doctorId']==doc['id'] for a in own),'Doctor appointment list is scoped to own patients')
                today_all=admin.req(f'/appointments?date={today}')['items']
                current=next(a for a in today_all if a['status']=='CheckedIn' and a['doctorId']==doc['id'])
                foreign=next(a for a in today_all if a['status']=='CheckedIn' and a['doctorId']!=doc['id'])
                doctor.req(f"/appointments/{foreign['id']}/start",'POST',{'version':foreign['version']},expected=403)
                reception.req(f"/appointments/{current['id']}/start",'POST',{'version':current['version']},expected=403)
                start_result=doctor.req(f"/appointments/{current['id']}/start",'POST',{'version':current['version']})
                check(start_result['status']=='InProgress','Only authorized doctor can start assigned consultation')
                enc=doctor.req(f"/appointments/{current['id']}/encounter")['encounter']
                payload={'complaint':'测试主诉','assessment':'测试评估','plan':'测试处置','followUpDate':str(tomorrow),'version':enc['version']}
                url=f"/appointments/{current['id']}/encounter"
                doctor.req(url,'PUT',{**payload,'complaint':' '},expected=400)
                doctor.req(url,'PUT',{**payload,'followUpDate':str(today)},expected=400)
                saved=doctor.req(url,'PUT',payload)
                doctor.req(url,'PUT',payload,expected=409)
                doctor.req(url+'?finalize=true','PUT',{**payload,'version':saved['version']})
                doctor.req(url+'?finalize=true','PUT',{**payload,'version':saved['version']+1},expected=409)
                check(doctor.req(url)['encounter']['finalizedUtc'] is not None,'Draft save, stale update rejection and immutable finalized encounter')
                invoices=admin.req('/invoices?status=Unpaid')['items']; invoice=next(i for i in invoices if i['appointmentId']==current['id'])
                reception.req(f"/invoices/{invoice['id']}/pay",'POST',{'method':'现金','version':invoice['version']})
                reception.req(f"/invoices/{invoice['id']}/pay",'POST',{'method':'现金','version':invoice['version']},expected=409)
                check(invoice['amountCents']==3000,'Finalization creates one invoice; duplicate collection rejected')
                check(any(f['appointmentId']==current['id'] for f in doctor.req('/followups')),'Saved follow-up appears in doctor plan')
                staff={'username':'newstaff','password':'NewStaff!2026abc','name':'测试前台','role':'Reception','department':'门诊服务'}
                admin.req('/staff','POST',staff)
                admin.req('/staff','POST',staff,expected=409)
                check(True,'Admin creates staff; duplicate usernames rejected')
                logs=admin.req('/audit')['items']; check(any(a['action']=='登记收款' for a in logs),'Audit log records business writes')
                admin.req('/auth/password','POST',{'currentPassword':'ClinicDemo!2026','newPassword':'ChangedPassword!2026'})
                other_session.req('/patients',expected=401)
                check(True,'Password change invalidates other logged-in sessions')
                process.terminate(); process.wait(timeout=10)
                process=start(temp,log)
                admin=Client(); admin.login('admin','ChangedPassword!2026')
                persisted=admin.req('/patients/'+str(p['id']))['patient']
                check(persisted['name']=='测试患者更新','Patient records persist after process restart')
                refreshed=admin.req('/dashboard');check(refreshed['completed']==dashboard['completed']+1,'Dashboard reflects completed consultation')
                locked=Client(); locked.refresh()
                for _ in range(5): locked.req('/auth/login','POST',{'username':'newstaff','password':'incorrect'},expected=401)
                locked.req('/auth/login','POST',{'username':'newstaff','password':'NewStaff!2026abc'},expected=401)
                check(True,'Five failed passwords lock the account even with the correct password')
                for _ in range(3): locked.req('/auth/login','POST',{'username':'unknown','password':'incorrect'},expected=401)
                locked.req('/auth/login','POST',{'username':'unknown','password':'incorrect'},expected=429)
                check(True,'Login rate limiter rejects excess attempts')
                print(f'\n{len(passed)} integration checks passed.',flush=True)
            finally:
                if process.poll() is None: process.terminate(); process.wait(timeout=10)

if __name__=='__main__': run()
