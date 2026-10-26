// AI: тести розбору сторінок work.ua (картки списку й сторінка вакансії → RawVacancy) (issue #62); перевірено: запуск dotnet test, звірка зі справжніми сторінками work.ua (жовтень 2026), самоперевірка автора (§8)

using Waterfall.Api.Workers;
using Waterfall.Repository.Entities;
using Waterfall.Services;
using Waterfall.Services.Interfaces;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Ingestion.Tests;

public class WorkUaPageTests
{
    private static readonly WorkUaParser WorkUa = new();

    // Скорочена розмітка справжніх карток зі списку вакансій work.ua (жовтень 2026); компанії й тексти вигадані.
    // Гаряча вакансія з вилкою зарплати, мітка «перевірена компанія», місто з відстанню до центру
    internal const string HotCard = """
        <div tabindex="0" class="card card-hover card-visited wordwrap job-link js-hot-block" >
          <div class="mt-lg sm:mt-xl"><ul class="list-unstyled my-0"><li class="no-style mr-xs mb-sm inline-block"><span class="label label-orange-light"><span class="glyphicon glyphicon-top glyphicon-fs-16 glyphicon-flame mr-xs"></span>Гаряча</span></li></ul></div>
          <div class="mb-lg mt-sm">
            <img width="100" height="50" alt="Kvitka Software" class="preview-img preview-img-logo" src="https://assets.work.ua/employer_design/1/company_logo_v1.png">
            <h2 class="my-0"><a tabindex="-1" href="/jobs/8051901/" title="Junior QA Engineer, вакансія від 9 жовтня 2026">Junior QA Engineer</a></h2>
          </div>
          <div><div class="text-indent">
            <span class="glyphicon glyphicon-hryvnia-fill glyphicon-fs-24 text-default-7" title="Зарплата"></span>
            <span class="strong-600">17&#8239;000&thinsp;–&thinsp;35&#8239;000&nbsp;грн</span><span class="text-default-7">&nbsp;·</span>
            <span class="text-default-7 nowrap js-salary-deduction"><span class="glyphicon glyphicon-tax glyphicon-fs-24" data-content="Зарплата після всіх відрахувань" data-toggle="popover"></span><span class="strong-600 ml-xs js-salary-deduction-text">Після всіх відрахувань</span></span>
          </div></div>
          <div class="mt-sm">
            <div class="text-indent"><span class="glyphicon glyphicon-company glyphicon-fs-24 text-default-7" title="Дані про компанію"></span> <span class="mr-xs"> <span class="strong-600">Kvitka Software</span></span><ul class="list-unstyled my-0 sm:mr-xs inline-block"><li class="no-style my-0 inline-block"><span class="label label-circle label-circle-default label-gray-lighter cursor-p" data-content="Дійсність компанії підтверджена Work.ua. <a href='/help/177/' class='bp-more' target='_blank'>Докладніше</a>" data-toggle="popover"><span class="glyphicon glyphicon-tick glyphicon-fs-16"></span></span></li></ul></div>
            <div class="text-indent mt-sm"><span class="glyphicon glyphicon-map-marker glyphicon-fs-24 text-default-7" title="Адреса роботи"></span><span class="">Тернопіль, </span><span class="distance-block unclickable-inner-block text-blue-link"><span > <a href='/jobs-it/?category=1' class='bp-more nowrap'>1,0&nbsp;км&nbsp;від&nbsp;центру</a> </span></span></div>
            <div class="text-indent mt-sm"><span class="glyphicon glyphicon-list glyphicon-fs-24 text-default-7" title="Умови й вимоги"></span> <span class="">Досвід від 1 року · Стандартний графік: 5/2</span></div>
          </div>
          <p class="ellipsis ellipsis-line ellipsis-line-2 text-default-7 mb-0"> Тестування вебзастосунку для онлайн-банкінгу&hellip;</p>
        </div>
        """;

    // Зарплату не вказано, є лише мітка «Вища за середню» (зі значком гривні, але без суми); робота дистанційна
    internal const string RemoteCard = """
        <div tabindex="0" class="card card-hover card-visited wordwrap job-link mt-sm sm:mt-lg" >
          <div class="mt-lg sm:mt-xl"><ul class="list-unstyled my-0"><li class="no-style mr-xs mb-sm inline-block"><span class="label label-green-50 cursor-p" data-content="Зарплата вища за середню для цієї посади." data-toggle="popover"> <span class="glyphicon glyphicon-hryvnia-fill glyphicon-top glyphicon-fs-16 mr-xs"></span> <span>Вища за середню</span> </span></li></ul></div>
          <div class="mb-lg mt-sm">
            <h2 class="my-0"><a tabindex="-1" href="/jobs/5476075/" title="Support Engineer (L2), вакансія від 22 жовтня 2026">Support Engineer (L2)</a></h2>
          </div>
          <div class="mt-sm">
            <div class="text-indent"><span class="glyphicon glyphicon-company glyphicon-fs-24 text-default-7" title="Дані про компанію"></span> <span class=""> <span class="strong-600">Lumos Fintech</span></span></div>
            <div class="text-indent mt-sm"><span class="glyphicon glyphicon-map-marker glyphicon-fs-24 text-default-7" title="Адреса роботи"></span><span class="">Дистанційно</span> </div>
          </div>
          <p class="ellipsis ellipsis-line ellipsis-line-2 text-default-7 mb-0"> Підтримка клієнтів у США&hellip;</p>
        </div>
        """;

    [Fact]
    public void ParseListPage_ReadsCards()
    {
        List<RawVacancy> jobs = WorkUa.ParseListPage(ListPage(HotCard, RemoteCard));

        Assert.Equal(2, jobs.Count);
        RawVacancy job = jobs[0];
        Assert.Equal(JobSource.WorkUa, job.Source);
        Assert.Equal("8051901", job.Id);
        Assert.Equal("https://www.work.ua/jobs/8051901/", job.Url);
        Assert.Equal("Junior QA Engineer", job.Title);
        Assert.Equal("Kvitka Software", job.Company); // без тексту мітки «перевірена компанія»
        Assert.Equal("17 000 – 35 000 грн", job.Salary);
        Assert.Equal("Тернопіль", job.Location);     // без «1,0 км від центру»
        Assert.Equal("09.10.2026", job.Posted);
        Assert.Contains("Тестування вебзастосунку", job.DescriptionHtml);
        Assert.Null(job.Tags); // навички — лише на сторінці вакансії
    }

    [Fact]
    public void ParseListPage_AboveAverageLabelIsNotSalary()
    {
        RawVacancy job = Assert.Single(WorkUa.ParseListPage(ListPage(RemoteCard)));

        Assert.Null(job.Salary);
        Assert.Equal("Дистанційно", job.Location);
        Assert.Equal("Lumos Fintech", job.Company);
        Assert.Equal("22.10.2026", job.Posted);
    }

    [Fact]
    public void ParseListPage_TakesTitleFromLinkHintWhenTextIsEmpty()
    {
        // Як у WorkUaParserService Дениса: якщо в посиланні немає тексту, назва є в підказці
        string card = Card(7, "QA Lead").Replace(">QA Lead</a>", "></a>", StringComparison.Ordinal);

        Assert.Equal("QA Lead", Assert.Single(WorkUa.ParseListPage(ListPage(card))).Title);
    }

    [Theory]
    [InlineData("<html><body>Вакансій не знайдено</body></html>")]      // за останньою сторінкою список порожній
    [InlineData("<div class=\"card job-link\"><h2><a href=\"/jobs/by-company/1/\">Компанія</a></h2></div>")] // не вакансія
    [InlineData("<div class=\"card job-linked\"><h2><a href=\"/jobs/1/\">Схожий клас</a></h2></div>")]
    [InlineData("<div class=\"card job-link\"><p>Рекламний блок без посилання</p></div>")]
    public void ParseListPage_PageWithoutJobs_ReturnsEmptyList(string html)
    {
        Assert.Empty(WorkUa.ParseListPage(html));
    }

    [Theory]
    [InlineData("вакансія від 1 січня 2026", "01.01.2026")]
    [InlineData("вакансія від 31 грудня 2025", "31.12.2025")]
    [InlineData("вакансія від 5 Травня 2026", "05.05.2026")]
    [InlineData("вакансія від 5 мая 2026", null)] // невідомий місяць — дату візьме нормалізація з моменту збору
    [InlineData("Без дати", null)]
    public void ParseListPage_ReadsPostedDateFromLinkHint(string hint, string? expected)
    {
        string card = Card(1, "QA").Replace("вакансія від 20 жовтня 2026", hint, StringComparison.Ordinal);

        Assert.Equal(expected, Assert.Single(WorkUa.ParseListPage(ListPage(card))).Posted);
    }

    [Fact]
    public void ParseJobPage_ReadsDetails()
    {
        RawVacancy details = WorkUa.ParseJobPage(JobPage(skills: ["Уважність", "Jira", "SQL &amp; NoSQL"]));

        Assert.Equal("Уважність, Jira, SQL & NoSQL", details.Tags);
        Assert.Equal("Повна зайнятість · Стандартний графік: 5/2", details.Employment);
        Assert.Equal("Досвід роботи від 1 року", details.Experience);
        Assert.Equal("Тестувальник", details.Category);
        Assert.Contains("<li>Досвід ручного тестування</li>", details.DescriptionHtml);
        Assert.Null(details.Title); // назву, компанію й зарплату вже дала картка
    }

    [Fact]
    public void ParseJobPage_SkillsAreOptional()
    {
        RawVacancy details = WorkUa.ParseJobPage(JobPage(skills: [], experience: null, breadcrumbs: ["Вакансії", "у Києві"]));

        Assert.Null(details.Tags);
        Assert.Null(details.Experience);
        Assert.Null(details.Category); // лише місто, без категорії
    }

    [Fact]
    public void ListAndJobPage_PassNormalization()
    {
        RawVacancy fromList = WorkUa.ParseListPage(ListPage(HotCard))[0];
        RawVacancy job = SiteCrawler.WithJobPage(fromList, WorkUa.ParseJobPage(JobPage(skills: ["Комунікація", "Jira"])));

        (VacancyDB? vacancy, string? error) = VacancyIngestionService.Normalize(job, new DateTimeOffset(2026, 10, 25, 10, 0, 0, TimeSpan.Zero));

        Assert.Null(error);
        Assert.NotNull(vacancy);
        Assert.Equal("WorkUa", vacancy.Source);
        Assert.Equal((17000m, 35000m, "UAH"), (vacancy.SalaryMin, vacancy.SalaryMax, vacancy.Currency));
        Assert.Equal(new DateTimeOffset(2026, 10, 9, 0, 0, 0, TimeSpan.Zero), vacancy.PublishedAt);
        Assert.Equal("Вимоги:\nДосвід ручного тестування", vacancy.Description); // повний опис зі сторінки, а не скорочений з картки
        Assert.Equal("[\"Комунікація\",\"Jira\"]", vacancy.SkillsJson);
        Assert.Equal("Тестувальник", vacancy.Category);
        Assert.Equal("Повна зайнятість · Стандартний графік: 5/2", vacancy.Schedule);
    }

    /// <summary>Сторінка списку work.ua з переданими картками.</summary>
    internal static string ListPage(params string[] cards) =>
        $"""<html><body><div id="pjax-jobs-list">{string.Join("\n", cards)}</div></body></html>""";

    /// <summary>Мінімальна картка вакансії work.ua з заданим id.</summary>
    internal static string Card(int id, string title = "Вакансія") => $"""
        <div class="card card-hover job-link">
          <h2 class="my-0"><a href="/jobs/{id}/" title="{title}, вакансія від 20 жовтня 2026">{title}</a></h2>
          <p class="ellipsis">Короткий опис</p>
        </div>
        """;

    /// <summary>
    /// Фрагмент справжньої сторінки вакансії work.ua (жовтень 2026): «хлібні крихти», шапка з графіком, опис
    /// і блок «Ключові вимоги і навички», де навички — необов'язкові мітки.
    /// </summary>
    internal static string JobPage(string[] skills, string? experience = "Досвід роботи від 1 року.", string[]? breadcrumbs = null)
    {
        string crumbs = string.Concat((breadcrumbs ?? ["Вакансії", "для дистанційної роботи", "Тестувальник"]).Select(name =>
            $"""<li itemprop="itemListElement" itemscope itemtype="http://schema.org/ListItem"><a itemprop="item" href="/jobs/"><span itemprop="name">{name}</span></a></li>"""));
        string requirement = experience is null ? string.Empty
            : $"""<li class="text-indent no-style mt-sm mb-0"><span class="glyphicon glyphicon-tick text-default glyphicon-large"></span> {experience} </li>""";
        string labels = string.Concat(skills.Select(skill =>
            $"""<li class="no-style label label-skill label-gray-100 mr-sm mb-xs mt-xs"><span class="ellipsis">{skill}</span></li>"""));
        return $"""
            <ol class="breadcrumb flex flex-align-center flex-wrap mt-0 hidden-xs" itemscope itemtype="http://schema.org/BreadcrumbList">{crumbs}</ol>
            <h1 class="h2 mt-xs mb-0" id="h1-name">Junior QA Engineer</h1>
            <ul class="list-unstyled sm:mt-xl mt-lg mb-0">
              <li class="text-indent no-style mt-sm mb-0"><span class="glyphicon glyphicon-hryvnia-fill text-default glyphicon-large" title="Зарплата"></span> <span class="strong-500">17&#8239;000&thinsp;–&thinsp;35&#8239;000 грн</span></li>
              <li class="text-indent no-style mt-sm mb-0"><span class="glyphicon glyphicon-remote text-default glyphicon-large" title="Місце роботи"></span> Дистанційна робота </li>
              <li class="text-indent no-style mt-sm mb-0"><span class="glyphicon glyphicon-schedule text-default glyphicon-large" title="Графік роботи"></span>Повна зайнятість<span class="text-default-7 inline-block ml-xs mr-xs">·</span>Стандартний графік: 5/2</li>
            </ul>
            <hr class="mt-xl"/>
            <h2 class="h3">Про вакансію</h2>
            <div id="job-description" class="company-description"><p><b>Вимоги</b>:</p><ul><li>Досвід ручного тестування</li></ul></div>
            <hr class="mt-xl">
            <h3 class="h3">Ключові вимоги і навички</h3>
            <ul class="list-unstyled mt-lg mb-0">{requirement}</ul>
            <div class="mt-lg flex flex-wrap"><ul class="flex flex-wrap list-unstyled w-100 my-0 pb-0 toggle-block overflow block-relative js-toggle-block">{labels}</ul></div>
            """;
    }
}
