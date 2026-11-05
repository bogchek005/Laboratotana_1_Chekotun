# ЛР 05 Докази перевірки безпеки браузера

## Джерела і межі доказів

## Стенд і Git-стан

|Параметр|Підтверджене значення|
|-|-|
|Автор|Богдан Чекотун, КБ-42|
|Рівень|Добре|
|Стек|SecureLab, ASP.NET Core, net10.0, EF Core, PostgreSQL, Windows PowerShell|
|API і штатний клієнт|http://localhost:5080|
|Дозволений probe|http://localhost:4173|
|Контрольний probe|http://localhost:4174|
|Автентифікація|Навчальні користувачі Alice і Morgan; локальна cookie-сесія|
|Браузер|Chromium/Chrome за інтерфейсом DevTools; точний номер версії у звіті не наведений|
|Точна версія SDK|У скриншотах не зафіксована; target framework net10.0 відомий із проєкту|
|Scaffold|lab-05-start-v1, успадковує v0.4.0|
|Vulnerable commit|695b71b09a4ddadfcad6e2c8f851a11fcd31d94c|
|Vulnerable із comment fixture|d5c54e3ae8b4de9ee23ea0984a7f97880df18244|
|Коміт safe DOM|8a6915a0ced5eab046f9960179efe3a0ff1608e0|
|Фінальний CORS/CSP commit|Hash у наданих матеріалах не зафіксований|
|v0.5.0 і main|Створення тегу й перенесення fixed state до main не підтверджені наданими матеріалами|

Hash safe DOM не називається підсумковим CORS/CSP commit: наступні зміни показано пізніше. Квитанція початкового архіву містить base\_commit d68ca896fbed33ef782912b108f0a06e7248580e; це поле квитанції, а не підміна hash фактичного vulnerable commit зі скриншота.

## Локальне відтворення стенда

У PowerShell із кореня starter-project, з власним SeedUsers\_\_Password, переданим поза Git:

```powershell
docker compose --env-file infra/.env.example -f infra/compose.yaml up -d --wait
dotnet run --project src/SecureLab.Api -- --reset-database
dotnet run --project src/SecureLab.Api
```

Reset очищає навчальні дані. Пароль і cookie у документах не зберігаються.

Два інші термінали з того самого кореня:

```powershell
py -3 -m http.server 4173 --bind localhost --directory demonstrations/cors-probe
```

```powershell
py -3 -m http.server 4174 --bind localhost --directory demonstrations/cors-probe
```

## Normal flow і карта

Вхід через /auth-lab.html: ім'я alice або morgan, власний навчальний пароль; зафіксовано Login: 204. На /browser-lab.html кнопка «Прочитати» викликає GET /api/incidents/{id}. Для Alice seed Guid — 20000000-0000-0000-0000-000000000001; зафіксовано GET: 200, опис і публічний коментар (рисунки 5–7).

\[Повна карта Description і comments\[].text](source-to-sink.md).

## Finding XSS

|Частина|Результат|
|-|-|
|Актив|Browser session і доступні поточному principal дані|
|Передумова|Контроль текстового поля та право іншого користувача прочитати інцидент|
|Першопричина|current.description і comment.text передаються у innerHTML у load()|
|Вплив у досліді|data-lab05-marker="executed" лише на локальному #lab05-marker-target|
|Класифікація|Stored XSS із небезпечною клієнтською інтерпретацією даних у DOM sink|
|Fix|createTextElement із textContent; replaceChildren та append|
|Retest|Текст marker збережено, img не створюється, marker-атрибут відсутній|
|Залишковий ризик|Інші sinks і контексти, майбутні зміни, сторонні scripts|

Marker з офіційного lab05-marker.txt встановлює лише локальний атрибут; не читає credentials. Вигляд рядка, зафіксований у досліді:

```html
<img src=x onerror="document.querySelector('#lab05-marker-target').setAttribute('data-lab05-marker','executed')">
```

## Description до та після

|Стан|Запис|Спостереження|Доказ|
|-|-|-|-|
|До fix, Alice|ca58c57c-cafd-406a-abef-d632d0a0376e|Marker у JSON, img у DOM, executed|Рисунки 14–17|
|До fix, Morgan|Той самий запис|GET: 200, executed під час наступного читання|Рисунки 18–20|
|Після fix, Alice|39cf8ce3-bdd7-4a58-9a6b-f521e332f4c2|Marker у Payload і GET JSON, textContent; img null; marker null|Рисунки 31–37|
|Після fix, Morgan|Той самий новий запис|GET: 200; marker текстом; img null; marker null|Рисунки 38–40|

Між дослідами використано reset і повторне створення за тією самою процедурою; старий та новий Guid не ототожнюються. Порівнюється значення marker, а не ідентичність записів. Прямий dump БД не надано; незмінність рядка підтверджено response JSON і POST Payload.

## Коментар до та після

Fixture ...000000000005, Incident Alice, Author Morgan, IsInternal=false, CreatedAtUtc 2026-08-01 09:35 UTC. До fix зафіксовано executed та img у #comments (рисунки 21–25); після fix — видимий текст, querySelector("img") → null і незмінний comments\[].text у JSON (рисунки 26–30).

## Позитивні перевірки

|Вхід|До fix|Після fix|Доказ|
|-|-|-|-|
|Апостроф і подвійні лапки|Звичайний текст не розрізняє sinks|Символи збережено|Рисунок 41|
|5 < 7 і 9 > 3|Не є повноцінним тестом HTML parsing|Текст збережено|Рисунок 42|
|Київ — Ω|Unicode сам по собі не доводить безпеку|Unicode збережено|Рисунок 43|
|**жирний** текст|Створюється b, видиме форматування|Теги видимі як текст|Рисунки 9–10, 44|
|3900 літер А|Створення та читання показано|Окремий повтор 3900 після fix не показаний|Рисунок 11|

## CORS до policy

Simple GET /health з 4173 і 4174 не надано JavaScript через відсутність Allow-Origin (рисунки 49–50). Запити з X-Lab-Cors-Probe викликали preflight error з обох origins (рисунки 51–52). За цими Console-знімками не підміняємо невидимий HTTP-статус OPTIONS припущенням.

## Development CORS policy

```csharp
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("Lab05BrowserProbe", policy =>
        {
            policy.WithOrigins("http://localhost:4173")
                  .WithMethods("GET")
                  .WithHeaders("X-Lab-Cors-Probe")
                  .AllowCredentials();
        });
    });
}
```

UseRouting → умовний UseCors → UseAuthentication → UseAuthorization → UseRateLimiter → static files. Дозволи не розширено до 4174 або wildcard. Policy навчальна й лише Development; відсутність реєстрації у Production випливає з умови в коді, окремий Production runtime-запуск у звіті не показаний.

## CORS після policy і preflight

|Origin і probe|Фактично зафіксовано|Доказ|
|-|-|-|
|4173 simple GET|JavaScript читає 200, ready|Рисунок 56|
|4173 header GET|JavaScript читає 200, ready|Рисунок 56|
|4174 simple/header|CORS і preflight error|Рисунок 57|
|Allowed GET /health|200, Allow-Origin: http://localhost:4173|Рисунок 58|
|Показаний OPTIONS /health|204; Request-Method GET; Request-Headers x-lab-cors-probe; allow headers відсутні|Рисунок 59|
|4173 без credentials до protected API|JavaScript читає 401|Рисунок 55|

Рисунок 59 не є доказом дозволеного preflight: у ньому немає Allow-Origin/Methods/Headers. Для повного DEL-01 ще варто зберегти окремий позитивний OPTIONS із 4173 з усіма allow headers і наступним GET. Успіх fetch із custom header зафіксовано, проте повний позитивний preflight trace цим не замінюється.

## Cookie-дослід

* З AllowCredentials(): 4173 після входу отримує 200 і список інцидентів; response містить exact Allow-Origin і Allow-Credentials: true (рисунки 60–62).
* 4174 не читає credentialed response (рисунок 63).
* Тимчасове вилучення AllowCredentials() залишає credentialed response недоступним JavaScript (рисунки 64–65).
* AllowCredentials() повернуто (рисунок 66).
* Без входу запит із 4173 повертає читаний 401 із CORS headers (рисунок 67).

Фактичні значення cookie не зберігаються. HttpOnly/SameSite/Secure не вигадуються: повної таблиці атрибутів у звіті немає. Порти localhost відрізняють origins, але не доводять cross-site поведінку SameSite.

## Direct HTTP

```powershell
curl.exe -i http://localhost:5080/health
curl.exe -i -H "Origin: http://localhost:4174" http://localhost:5080/health
curl.exe -i http://localhost:5080/api/incidents
```

Отримано відповідно 200, 200 без Allow-Origin і 401 (рисунки 68–70). Direct client не застосовує browser SOP, але підлягає server authentication/authorization.

## CSP і browser headers

За Network власні browser-lab.js, auth-lab.js, app.js отримуються з localhost:5080. Development також додає aspnetcore-browser-refresh.js (рисунки 71–76). Завантаження CSS окремим знімком не підтверджено; style-src self відповідає локальним стилям baseline.

```text
default-src 'self'; script-src 'self'; style-src 'self'; connect-src 'self'; img-src 'self'; object-src 'none'; base-uri 'self'; frame-ancestors 'none'
```

|Контроль|Потреба та пояснення|Доказ|
|-|-|-|
|script-src self|Власні JS; без unsafe-inline|71–76, 79–80|
|connect-src self|Same-origin API основного клієнта|82–84|
|img-src self|Не потрібні images із probe origin|Блокування 4173/x.png, 81|
|object-src none|Об'єкти не потрібні|79–80|
|base-uri self|Обмежує підміну базової адреси|79–80|
|frame-ancestors none|Вбудовування клієнта не потрібне|80, 85|
|nosniff|Обмеження MIME sniffing|80, 85–87|
|no-referrer|Клієнт не потребує передавання Referer|80, 85–87|

CSP встановлено в існуючій middleware заголовків. Development /scalar виключено через окремий сторонній UI; основний клієнт цим не послаблено. Enforcement підтверджено заголовком і реальною CSP-відмовою для new Image(). Report-only для «Добре» не вимагався та не заявляється виконаним. Під CSP працюють список/деталі, seed-картка і створення нейтрального інциденту (82–84).

## REG-XSS-01

Передумови: відомі vulnerable і safe DOM commits вище; API 5080; власний пароль поза Git; окремі сеанси Alice/Morgan; Disable cache.

1. За потреби reset БД і запуск API за командами стенда. Врахувати втрату попередніх створених записів.
2. Увійти Alice через /auth-lab.html; відкрити /browser-lab.html і перезавантажити.
3. Перевірити початковий marker-атрибут: null.
4. Створити інцидент із новою назвою й marker з офіційного TXT; записати Guid.
5. Порівняти POST Payload description і GET response description.
6. Перевірити textContent, відсутність img у #description та відсутність marker-атрибута.
7. В окремому сеансі увійти Morgan, прочитати той самий Guid, повторити перевірки.
8. Перезавантажити сторінку, прочитати seed Alice ...000000000001 і перевірити marker-коментар ...000000000005.
9. Повторити лапки, <, >, Unicode, HTML-подібний рядок; повтор 3900 після fix ще потрібно зафіксувати.

```javascript
document.querySelector("#description").textContent;
document.querySelector("#description").querySelector("img");
document.querySelector("#comments").querySelector("img");
document.querySelector("#lab05-marker-target").getAttribute("data-lab05-marker");
```

Негативний контроль: у vulnerable стані executed та img; після safe fix null та текстові вузли. Докази 13–44. Це ручна регресія, не заявлений automated DOM test.

## REG-CORS-01

Передумови: API Development 5080, probes 4173/4174, exact policy з AllowCredentials.

На кожному probe виконати:

```javascript
await fetch("http://localhost:5080/health").then(async r => ({status:r.status, body:await r.text()}));
await fetch("http://localhost:5080/health?probe=final", {
  headers: {"X-Lab-Cors-Probe":"stage-3"}
}).then(async r => ({status:r.status, body:await r.text()}));
```

На 4173 очікується читаний 200; на 4174 — CORS-відмова. Зберегти окремі OPTIONS: Origin, Request-Method, Request-Headers, status і Allow-\*; потім actual GET. Якщо працює preflight cache, змінити query або відкрити чистий сеанс. Status 204 сам по собі не доводить дозвіл.

Після входу Alice у тому самому профілі на 4173 виконати:

```javascript
await fetch("http://localhost:5080/api/incidents", {credentials:"include"})
  .then(async r => ({status:r.status, body:await r.text()}));
```

Повторити з 4174, потім у чистому сеансі без входу на 4173. Негативний контроль — тимчасово прибрати AllowCredentials, потім обов'язково повернути й перезапустити API.

Фінальне порівняння:

```powershell
curl.exe -i -H "Origin: http://localhost:4173" http://localhost:5080/health
curl.exe -i -H "Origin: http://localhost:4174" http://localhost:5080/health
```

Обидва 200; лише 4173 має Allow-Origin і Allow-Credentials (рисунки 86–87). Браузерні результати — 49–67; curl їх не замінює.

## Підсумок підтвердження і завершення

Підтверджено safe DOM для двох полів, retest Alice/Morgan, позитивні короткі рядки, exact Development CORS, cookie-негативні контролі, direct HTTP, enforcement CSP і headers.

```powershell
dotnet --version
dotnet build --configuration Release
dotnet test tests/SecureLab.Api.Tests/SecureLab.Api.Tests.csproj --configuration Release
git status --short
git log --oneline --decorate -10
```

