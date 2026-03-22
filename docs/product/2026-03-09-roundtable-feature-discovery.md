# Product Roundtable: Feature Discovery Session

**Date:** 2026-03-09
**Participants:** Systems Architect, Business Analyst, Panel of Nutritionists & Assistants
**Method:** Simulated stakeholder roundtable informed by deep codebase analysis

---

## Participant Profiles

- **SA** -- Systems Architect
- **BA** -- Business Analyst (Facilitator)
- **Dr. Priya** -- Solo nutritionist, 3 years in practice, ~60 active clients
- **Marco** -- Nutritionist at a 4-person clinic, sports nutrition focus
- **Jen** -- Administrative assistant at a family nutrition practice
- **Aisha** -- Registered dietitian, pediatric specialist, privacy-conscious

---

## 1. The Daily Grind

**BA:** Walk me through your morning when you open Nutrir. What's the first thing you do, and where does it get frustrating?

**Dr. Priya:** I check the dashboard for today's appointments. That part's great -- I can see who's coming in. But then I need to prep. I click into each client, check their health profile, look at their current meal plan, review progress entries... that's three or four pages per client. For five appointments, I'm spending 20 minutes just *navigating*.

**SA:** There's no "appointment prep view" -- no single screen that aggregates the client's health snapshot, active meal plan, recent progress, and any notes from last session. The data is all there, it's just spread across separate pages with no appointment-centric view tying them together.

**Marco:** Same problem but worse for me. I share clients with other practitioners at our clinic. When I open the dashboard, I see *everyone's* appointments -- all four practitioners. There's no "my day" vs. "clinic day" filter. The metrics count all clients across the whole practice too. It's noise.

**SA:** Confirmed. `DashboardService.GetTodaysAppointmentsAsync()` queries all appointments with no practitioner filter. `GetMetricsAsync()` counts all clients globally. `Client.PrimaryNutritionistId` exists but nothing scopes views to it. This is the single biggest architectural barrier to multi-practitioner use.

---

## 2. Meal Plan Creation Is Brutal

**Dr. Priya:** It's the most painful part of the app. I type every food item by hand. *Every. Single. One.* "Grilled chicken breast" -- then I manually enter 165 calories, 31g protein, 0g carbs, 3.6g fat. For a 7-day plan with 4 meals a day and 3-4 items per meal, that's potentially 100+ items I'm hand-entering nutritional data for.

**Aisha:** And I make mistakes. I once put 31g of fat instead of protein on a chicken entry. The client followed the plan for two weeks before I caught it. There's no validation, no "does this look right?" check.

**SA:** The `MealItem` entity stores `FoodName` as free text with manual `CaloriesKcal`, `ProteinG`, `CarbsG`, `FatG`. There's a `FoodDatabase.cs` in the seed data generator with ~90 foods and their macros, but it's completely inaccessible to the running application. No food search, no autocomplete, no nutritional lookup.

**Dr. Priya:** And I make the *same* plans over and over. I have maybe 10 "base" plans -- weight loss 1200 cal, weight loss 1500 cal, maintenance 2000 cal, high-protein athletic... I recreate them from scratch every time. There's a "Duplicate" button which helps, but it copies a *client-specific* plan, not a generic template.

**Marco:** For sports nutrition, I also need micronutrients -- iron, calcium, vitamin D, B12. The current macro fields are protein/carbs/fat/calories only. I track micros in a separate spreadsheet.

**Aisha:** For my pediatric clients, portion sizes matter a lot. "1 cup" of rice vs. "1/2 cup" is a big deal for a 6-year-old. There's no portion size field -- just a generic "quantity" string.

---

## 3. The Appointment-to-Notes Dead End

**Dr. Priya:** After an appointment, nothing happens in the app. I mark it "Completed" and then... that's it. There's no prompt to write session notes. I have to remember to navigate to the client's progress page and create a new entry. Half the time I forget and do it hours later.

**Jen:** From the admin side, practitioners mark appointments complete but there's no way for me to check if notes were filed. I can't run a report that says "these 12 completed appointments have no associated progress entry."

**SA:** Confirmed structural gap. There's no foreign key between `Appointment` and `ProgressEntry`. No workflow trigger on status change. The two domains are completely disconnected.

**Aisha:** I'd love it if completing an appointment auto-created a draft progress entry with structured template -- "Session notes," "Client-reported adherence," "Measurements taken," "Plan adjustments," "Follow-up actions."

**Marco:** Adherence score. Did the client follow 80% of the meal plan? 50%? That's the most important thing I track and there's nowhere to put it.

**Dr. Priya:** Follow-up action items. "Increase water intake," "Try meal prepping Sundays," "Recheck weight in 2 weeks." Right now I put these in the notes field and pray I remember to check them next visit.

---

## 4. Client Communication Is Manual

**Dr. Priya:** I copy-paste meal plans into emails. I send reminder texts from my phone. Nothing is tracked in Nutrir.

**Jen:** I spend probably an hour a day sending appointment reminders manually. I check tomorrow's schedule, look up each client's email, and send individual reminder emails. Every. Single. Day.

**SA:** Email infrastructure exists -- `IEmailService` with MailKit/Gmail SMTP is fully wired. It's used for intake form links and there's recent work on appointment reminders. But beyond that: no meal plan delivery emails, no post-appointment follow-ups, no batch operations. And critically, no in-app messaging at all.

**Jen:** Even just automated "Your appointment is tomorrow at 2 PM" emails would save me 5 hours a week. And if a client cancels, I have to manually call people on the waitlist -- except there is no waitlist.

**Marco:** My clinic needs SMS. Half our clients are athletes under 30 -- they don't check email.

**Dr. Priya:** I'd settle for the ability to email a meal plan PDF directly from the app. Right now I export the PDF, open my email client, attach it, type the client's email address... it's 6 steps for something that should be one button.

---

## 5. Clients Are Invisible

**Dr. Priya:** Clients see an intake form before their first appointment. That's it. After that, they get nothing from the platform.

**Aisha:** Parents ask me constantly -- "Can I see the meal plan online?" No. "Can I log my child's weight between visits?" No. "Can I message you a quick question?" No. Email me.

**Marco:** My athletes want to track their own measurements -- weight, body fat, performance metrics. They're already using MyFitnessPal and Fitbit. They'd love to self-report into *my* system so I can see it before their appointment.

**SA:** The `Client` role exists in the database seeder -- one of four roles created at startup. But zero pages use it. Every authenticated page restricts to `"Admin,Nutritionist,Assistant"`. The architecture is *ready* for a client portal but nothing has been built.

**Aisha:** View my meal plan. View my upcoming appointments. That's it for v1.

**Dr. Priya:** Add self-reported progress entries -- weight, maybe a "how are you feeling 1-5" scale. That alone would be transformative.

**Marco:** If clients can see their progress chart line going down, adherence goes up. It's a motivation tool.

---

## 6. Quick Wins: Low Effort, High Impact

**Jen:** Recurring appointments. I book the same client every Tuesday at 10 AM for 8 weeks. Right now I create 8 individual appointments. A "repeat weekly for X weeks" option would save me so much time.

**Dr. Priya:** When I add a new allergy to a client's profile, their existing active meal plans don't get re-flagged. I found out the hard way.

**SA:** Confirmed. `IAllergenCheckService.CheckAsync()` is on-demand only. No event-driven re-check when `ClientHealthProfileService` updates allergies.

**Marco:** Appointment overlap detection. I've double-booked myself twice. The availability system exists but doesn't actually prevent booking conflicts at the service layer.

**SA:** `AvailabilityService` calculates available slots correctly, and the UI injects it, but `AppointmentService.CreateAsync()` never calls it. The AI assistant's `create_appointment` tool also bypasses it completely. Availability is advisory, not enforced.

**Aisha:** Auto-expiring meal plans. When a plan's end date passes, it should automatically archive. I have "active" plans from 6 months ago cluttering client profiles.

**Jen:** A "notes for next visit" field on the appointment. When I'm booking follow-ups, the practitioner tells me "ask them about the elimination diet" and I have nowhere to put that.

**Dr. Priya:** Dark mode. I see clients until 7 PM and the bright white screen at the end of a long day is brutal.

---

## 7. Major Enhancements: The Big Bets

**Marco:** AI-powered meal plan generation. I tell the system: "Generate a 7-day, 2200 calorie, high-protein plan for a 25-year-old male athlete with a dairy allergy." And it builds the whole thing. The AI assistant already has 38 tools, but none of them can *generate* a complete meal plan.

**SA:** The AI infrastructure is there -- `AiAgentService` with streaming, tool execution, and confirmation flows. But the tools are all CRUD operations. There's no generative tool that combines client health profile data, nutritional targets, and food database knowledge to produce a plan.

**Dr. Priya:** Outcome tracking across my entire practice. "Of clients who followed Plan X for 3+ months, what was the average weight change?" Right now progress data is per-client. There's no aggregate view.

**Aisha:** Integration with lab results. My pediatric clients get bloodwork -- iron levels, vitamin D, metabolic panels. I want to import those and correlate them with dietary interventions.

**Marco:** Wearable data sync. My athletes wear Garmin watches and Withings scales. The `MetricType` enum already has weight, body fat, heart rate, blood pressure. Auto-importing from device APIs would replace manual data entry *and* give me daily data points instead of weekly ones.

**Jen:** A proper waitlist system. When someone cancels, I manually call through a mental list. If cancellations auto-populated a waitlist notification, that would fill gaps that currently become lost revenue.

**Dr. Priya:** Client engagement scoring. Flag clients who are going cold -- no appointments in 30 days, haven't submitted progress, meal plan expired. A "clients at risk of disengaging" widget on the dashboard.

**SA:** The data for this exists today. `Client.CreatedAt`, last appointment date, last progress entry, meal plan expiry -- it's all queryable. But no report computes it. Same with practitioner utilization -- booked hours vs. available hours is trivially calculable but there's no report for it.

---

## 8. The Privacy Table

**Aisha:** Field-level encryption. Client health data -- medical conditions, medications, allergies, clinical notes -- is stored as plain text in PostgreSQL. PHIPA in Ontario and HIA in Alberta both expect reasonable safeguards on health information.

**SA:** The compliance docs acknowledge this -- field encryption with AES-256-GCM is listed as a v2 requirement. There's also no breach response plan documented (legal requirement under PIPEDA/PHIPA/HIA) and no data retention/purge workflow.

**Aisha:** The retention one worries me. In 7 years, when a client's data should be purged, will anyone remember? There needs to be an automated flag.

---

## 9. The AI Opportunity

**Dr. Priya:** I use the AI to create clients faster. But that's about it.

**Marco:** Can it say "show me all clients who haven't had an appointment in 30 days"?

**SA:** The `list_clients` tool exists but filtering is basic. There's no "inactive clients" filter or date-based query on last appointment. More broadly -- the AI has *no generative capabilities*. It can't analyze progress trends, suggest meal plan adjustments, draft follow-up emails, or generate session note templates.

**Aisha:** "Summarize this client's progress over the last 3 months and flag anything concerning." A pre-appointment briefing generated by AI.

**Dr. Priya:** "Draft a follow-up email to this client with their updated meal plan attached." One command instead of 6 steps.

**Jen:** "Find an available slot for a 60-minute follow-up with Dr. Priya next week." Right now I flip between the calendar page and the availability page.

---

## Synthesis

### Repetitive Tasks Killing Productivity

| Task | Who | Frequency | Potential Fix |
|------|-----|-----------|---------------|
| Manual food/macro data entry | All practitioners | Every meal plan | Food database with autocomplete |
| Sending appointment reminders | Jen | Daily | Automated email/SMS reminders |
| Recreating similar meal plans | Dr. Priya | Weekly | Reusable plan templates |
| Navigating between pages to prep | All | Before every appointment | Appointment prep view |
| Manually archiving expired plans | Aisha | Monthly | Auto-archive on end date |
| Creating recurring appointments | Jen | Weekly | Recurring appointment series |
| Emailing meal plan PDFs | Dr. Priya | Per client | One-click email from plan page |

### Workflow Gaps (Things That Should Connect But Don't)

| Gap | Impact |
|-----|--------|
| Appointment completion --> Session notes | Notes are forgotten or delayed |
| Client allergy update --> Meal plan re-check | Allergen warnings missed on active plans |
| Availability rules --> Appointment creation | Double-bookings possible |
| Progress goals --> Measurements | No auto-detection of goal achievement |
| Meal plan targets --> Actual item totals | No running total vs. daily target |

### Missing Intelligence

| Insight | Data Exists? | Report Exists? |
|---------|-------------|----------------|
| Practitioner utilization rate | Yes | No |
| Client retention/churn | Yes | No |
| Which meal plan approaches work best | Partially | No |
| Clients at risk of disengaging | Yes | No |
| Practice-wide health outcome trends | Yes | No |
| AI tool effectiveness | Yes | No |

### New Capabilities (Major)

| Feature | Champion | Value |
|---------|----------|-------|
| Client portal (view plans, log progress) | All | Client engagement + adherence |
| AI meal plan generation | Marco | 30min --> 5min plan creation |
| Food/nutrition database | All | Accuracy + speed |
| Wearable data sync | Marco | Daily data vs. weekly manual entry |
| Lab result integration | Aisha | Clinical evidence loop |
| In-app messaging | Dr. Priya | Replace ad-hoc email |
| Waitlist management | Jen | Fill cancellation gaps |
| Client engagement scoring | Dr. Priya | Proactive retention |
| Multi-practitioner scoping | Marco | Required for any clinic >1 person |
| Structured session notes | Aisha | Clinical documentation quality |

### Quick Wins (< 1 week each)

1. Enforce appointment overlap detection at service layer
2. Auto-archive expired meal plans (background job)
3. Re-check allergens when client profile changes
4. "Email this plan" button on meal plan detail page
5. Recurring appointment creation (repeat weekly for N weeks)
6. Auto-suggest goal achievement when measurement hits target
7. "Appointment prep" link that opens client detail in context
8. Pre-appointment notes field on appointment entity

### Priority Picks (One Thing Each)

- **Dr. Priya:** Food database -- cuts meal planning time in half, eliminates data entry errors
- **Marco:** Multi-practitioner scoping -- without it, clinics can't grow
- **Jen:** Automated appointment reminders -- 5 hours/week back immediately
- **Aisha:** Structured session notes triggered by appointment completion -- clinical documentation gap
- **SA:** Food database -- prerequisite for AI meal plan generation, accurate macro tracking, allergen improvements, and client-facing plan views
