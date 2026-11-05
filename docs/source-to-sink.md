# ЛР 05 Карта source-to-sink

Рівень: «Добре». Чекотун Богдан, КБ-42. Предметні поля — Incident.Description і IncidentComment.Text. Обидва є plain text; formatted HTML не передбачено. Шляхи нижче відраховано від кореня starter-project.

## Потік Incident.Description

|Межа|Файл або операція|Значення та перетворення|Довіра|
|-|-|-|-|
|Source|src/SecureLab.Api/Client/browser-lab.html, textarea name="description"|Користувач Alice вводить опис|Контрольоване користувачем|
|Формування request|Client/browser-lab.js, submit форми #edit|JSON.stringify; JSON-поле description; severity Low; occurredAtUtc new Date().toISOString()|Недовірене значення|
|HTTP|POST /api/incidents|Content-Type application/json|Автентифікація не робить текст довіреним HTML|
|Validation|Scaffolding/Lab02Endpoints.cs, MapLab02Endpoints|Required, довжина до 4000; для High/Critical додаткове предметне правило; Trim()|Структурно перевірений plain text|
|Storage|Incident.Description у PostgreSQL|Рядок після Trim(); marker не має крайових пробілів|Збережене значення користувача|
|Доступ|Presentation/Endpoints/IncidentEndpoints.cs, GetDetailsAsync|Principal, роль і доступ до ресурсу|Контроль права читати ресурс|
|Читання|Application/Incidents/IncidentQueries.cs, GetDetailsAsync|EF projection у IncidentDetailsResponse.Description|Без HTML-санітизації|
|Response|Presentation/Contracts/IncidentResponses.cs|JSON-поле description; JSON escaping|Значення залишається недовіреним для HTML sink|
|Client|Client/browser-lab.js, load(), response.json()|current.description; відновлення Unicode-рядка з JSON|Збережене значення користувача|
|Sink до fix|#description.innerHTML = current.description|HTML parser створює img та event handler|Небезпечний HTML-контекст|
|Sink після fix|#description.replaceChildren(createTextElement("p", current.description))|Helper створює елемент, потім element.textContent = text ?? ""|Текстовий вузол|
|Поле редагування|#edit.elements.description.value = current.description|Запис у value, без HTML parsing|Значення textarea|

Докази: рисунки 8–17, 31–37, 38–40.

## Потік IncidentComment.Text

|Межа|Реалізація|Значення та перетворення|
|-|-|-|
|Source у досліді|Data/DbSeeder.cs, SeedAsync, Lab05Marker|Сталий відтворюваний marker; у продукті текст контролював би автор коментаря|
|Fixture|IncidentComment Id 30000000-0000-0000-0000-000000000005|Incident = aliceIncident, Author = morgan, IsInternal = false|
|Storage|IncidentComment.Text|Канонічний marker збережений як рядок|
|Читання|IncidentQueries.GetDetailsAsync|Публічні comments, проєкція IncidentCommentResponse.Text|
|JSON|comments\[].text|Unicode-рядок marker|
|Client до fix|Цикл for (const comment of current.comments ?? \[]) у load()|paragraph.innerHTML = comment.text|
|Sink до fix|HTML parser|Створюється img, виконується onerror|
|Client після fix|comments.append(createTextElement("p", comment.text))|Спільний helper із textContent|
|Sink після fix|Текстовий вузол|Видимий marker, без створення img|

Внутрішній коментар ...000000000003 не замінювався. У надісланому baseline деталей фільтр `.Where(comment => !comment.IsInternal)` відокремлює внутрішні коментарі. Докази: рисунки 21–30.

## Інвентаризація власних DOM sinks у досліджуваному flow

|Місце|Походження|Контекст|Рішення|
|-|-|-|-|
|browser-lab.js: description.innerHTML|Збережений користувацький Description|HTML|Замінено спільним helper із textContent|
|browser-lab.js: paragraph.innerHTML|Збережений текст коментаря|HTML|Замінено тим самим helper|
|browser-lab.js: #title.textContent|Збережена користувацька назва|Text|Безпечний текстовий sink|
|browser-lab.js: status.textContent|Code-owned префікс і числовий HTTP-статус|Text|Залишено|
|browser-lab.js: textarea.value|Description з API|Значення поля форми|Залишено|
|browser-lab.js: encodeURIComponent(id) у fetch URL|Guid із поля користувача|Компонент шляху same-origin API|Кодування компонента; серверний маршрут {id:guid}|
|app.js: createTextElement(tagName, text, className)|Текстові поля DTO|Text|Зразок safe DOM у штатному клієнті|
|auth-lab.js: result.textContent|HTTP-статус і JSON-профіль|Text|Без HTML parsing|
|demonstrations/cors-probe/probe.js: output.textContent|Відповідь fetch|Text|Без HTML parsing|



## Гіпотеза і результат

Description із форми через POST і БД повертався у JSON та передавався в innerHTML, тому міг інтерпретуватися браузером як HTML. Marker виконав локальну дію в Alice і під час читання Morgan. Після зміни sink дані залишилися у JSON, але відображалися текстом. Аналогічний результат отримано для публічного коментаря.

Sanitizer не додається: вимоги приймати formatted HTML немає. JSON escaping і required/length validation не замінюють правильний контекст виведення.

