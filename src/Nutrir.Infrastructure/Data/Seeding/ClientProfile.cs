using Nutrir.Core.Enums;

namespace Nutrir.Infrastructure.Data.Seeding;

public record ClientProfile(
    string Tag,
    string[] NoteTemplates,
    (string Title, string Description)[] MealPlanTemplates,
    (int MinCalories, int MaxCalories, int MinProtein, int MaxProtein, int MinCarbs, int MaxCarbs, int MinFat, int MaxFat) MacroTargets,
    GoalType[] RelevantGoalTypes,
    MetricType[] RelevantMetrics,
    string[] GoalTitleTemplates,
    string[] FoodPoolTags,
    (string Name, AllergySeverity Severity, AllergyType Type)[] AllergyPool,
    (string Name, string? Dosage, string? Frequency, string? PrescribedFor)[] MedicationPool,
    (string Name, string? Code, ConditionStatus Status, string? Notes)[] ConditionPool,
    DietaryRestrictionType[] RestrictionPool)
{
    public static IReadOnlyList<ClientProfile> All { get; } = new[]
    {
        new ClientProfile(
            Tag: "weight-management",
            NoteTemplates:
            [
                "Client reports improved adherence to portion control this week. Energy levels are stable and cravings have decreased since increasing protein at breakfast.",
                "Discussed strategies for eating out while staying within calorie targets. Client is motivated but finds weekends challenging due to social events.",
                "Weight trending downward at a healthy rate. Reviewed food diary — snacking after dinner remains the primary area for improvement.",
                "Client expressed frustration with a plateau this week. Reassured that fluctuations are normal and adjusted macro split slightly to break through."
            ],
            MealPlanTemplates:
            [
                ("Calorie-Controlled Balanced Plan", "A structured meal plan emphasizing whole foods with moderate calorie restriction to support gradual, sustainable weight loss."),
                ("High-Protein Weight Loss Plan", "Protein-forward plan designed to preserve lean mass during a caloric deficit while promoting satiety between meals."),
                ("Portion-Managed Mediterranean Plan", "Mediterranean-style eating pattern with pre-portioned meals to simplify calorie tracking and encourage nutrient density.")
            ],
            MacroTargets: (1800, 2000, 120, 150, 180, 220, 50, 70),
            RelevantGoalTypes: [GoalType.Weight, GoalType.BodyComposition],
            RelevantMetrics: [MetricType.Weight, MetricType.BodyFatPercentage, MetricType.WaistCircumference],
            GoalTitleTemplates:
            [
                "Lose 10 lbs over 12 weeks",
                "Reduce waist circumference by 2 inches",
                "Reach target weight of 160 lbs"
            ],
            FoodPoolTags: ["general", "high-protein", "low-calorie"],
            AllergyPool:
            [
                ("Peanuts", AllergySeverity.Severe, AllergyType.Food),
                ("Shellfish", AllergySeverity.Moderate, AllergyType.Food),
            ],
            MedicationPool:
            [
                ("Orlistat", "120 mg", "Three times daily with meals", "Weight management"),
                ("Metformin", "500 mg", "Twice daily", "Insulin resistance"),
            ],
            ConditionPool:
            [
                ("Obesity", "E66.0", ConditionStatus.Active, "BMI > 30"),
                ("Hypertension", "I10", ConditionStatus.Managed, "Controlled with lifestyle modifications"),
            ],
            RestrictionPool: []),

        new ClientProfile(
            Tag: "diabetes",
            NoteTemplates:
            [
                "Reviewed recent blood glucose logs. Fasting glucose has improved since switching to lower-GI carbohydrate sources at dinner.",
                "Client reports fewer post-meal spikes after incorporating the recommended pairing of protein with carbohydrates. A1C recheck scheduled for next month.",
                "Discussed the glycemic index concept in more detail. Client is now reading nutrition labels and choosing whole grain options consistently.",
                "Blood pressure remains slightly elevated. Reinforced sodium reduction strategies and encouraged continued adherence to the DASH-style modifications."
            ],
            MealPlanTemplates:
            [
                ("Blood Sugar Stabilization Plan", "Low-glycemic meal plan with balanced macronutrient distribution across five small meals to minimize blood glucose fluctuations."),
                ("Diabetic-Friendly Mediterranean Plan", "Mediterranean-inspired plan emphasizing healthy fats, legumes, and non-starchy vegetables to support glycemic control."),
                ("Carb-Controlled Daily Plan", "Structured plan with consistent carbohydrate portions at each meal to support predictable insulin response and blood sugar management.")
            ],
            MacroTargets: (1600, 1800, 100, 130, 150, 180, 50, 65),
            RelevantGoalTypes: [GoalType.Weight, GoalType.Dietary],
            RelevantMetrics: [MetricType.Weight, MetricType.BloodPressureSystolic, MetricType.BloodPressureDiastolic],
            GoalTitleTemplates:
            [
                "Stabilize fasting blood glucose below 7 mmol/L",
                "Reduce A1C by 0.5% over 3 months",
                "Achieve consistent carb intake of 45g per meal"
            ],
            FoodPoolTags: ["general", "low-gi", "diabetic-friendly"],
            AllergyPool:
            [
                ("Peanuts", AllergySeverity.Moderate, AllergyType.Food),
                ("Penicillin", AllergySeverity.Severe, AllergyType.Drug),
            ],
            MedicationPool:
            [
                ("Metformin", "1000 mg", "Twice daily", "Type 2 diabetes"),
                ("Insulin Glargine", "20 units", "Once daily at bedtime", "Type 2 diabetes"),
                ("Gliclazide", "80 mg", "Once daily", "Type 2 diabetes"),
            ],
            ConditionPool:
            [
                ("Type 2 Diabetes", "E11", ConditionStatus.Active, "Diagnosed 3 years ago"),
                ("Hypertension", "I10", ConditionStatus.Managed, null),
                ("Dyslipidemia", "E78.5", ConditionStatus.Active, "Elevated LDL cholesterol"),
            ],
            RestrictionPool: [DietaryRestrictionType.LowSodium]),

        new ClientProfile(
            Tag: "sports-nutrition",
            NoteTemplates:
            [
                "Client training volume has increased ahead of competition season. Adjusted caloric intake upward and added a post-workout recovery snack to support performance.",
                "Discussed pre-competition fuelling strategy. Client will trial the carb-loading protocol this weekend during a practice event.",
                "Reviewed hydration habits — client was under-consuming fluids during long sessions. Provided an electrolyte strategy for training days over 90 minutes.",
                "Recovery has improved since adding casein protein before bed. Client reports less muscle soreness and better sleep quality."
            ],
            MealPlanTemplates:
            [
                ("Performance Fuelling Plan", "High-energy plan with periodized carbohydrate intake timed around training sessions to maximize performance and recovery."),
                ("Muscle Building Nutrition Plan", "Caloric surplus plan with elevated protein targets and strategic nutrient timing to support hypertrophy during strength training phases."),
                ("Competition Prep Meal Plan", "Structured plan for the final week before competition including carb-loading, hydration, and race-day nutrition protocols.")
            ],
            MacroTargets: (2800, 3200, 150, 180, 350, 420, 70, 90),
            RelevantGoalTypes: [GoalType.BodyComposition, GoalType.Custom],
            RelevantMetrics: [MetricType.Weight, MetricType.BodyFatPercentage],
            GoalTitleTemplates:
            [
                "Increase lean mass by 3 kg during off-season",
                "Optimize race-day fuelling protocol",
                "Reduce body fat to 12% while maintaining strength"
            ],
            FoodPoolTags: ["general", "high-protein", "high-carb", "energy-dense"],
            AllergyPool:
            [
                ("Dairy", AllergySeverity.Moderate, AllergyType.Food),
                ("Soy", AllergySeverity.Mild, AllergyType.Food),
            ],
            MedicationPool:
            [
                ("Creatine Monohydrate", "5 g", "Once daily", "Athletic performance"),
                ("Vitamin D", "2000 IU", "Once daily", "Bone health and recovery"),
            ],
            ConditionPool:
            [
                ("Exercise-induced asthma", "J45.990", ConditionStatus.Managed, "Uses inhaler before exercise"),
            ],
            RestrictionPool: [DietaryRestrictionType.DairyFree]),

        new ClientProfile(
            Tag: "prenatal",
            NoteTemplates:
            [
                "Client is in second trimester and reports reduced nausea. Appetite has returned and we are now able to increase variety in the meal plan.",
                "Reviewed iron and folate intake from food sources. Client is meeting targets through diet plus prenatal supplement. No concerns at this time.",
                "Discussed safe fish consumption guidelines and omega-3 sources. Client will aim for two servings of low-mercury fish per week.",
                "Weight gain is tracking within recommended range. Client is feeling well and tolerating the increased caloric intake without discomfort."
            ],
            MealPlanTemplates:
            [
                ("Second Trimester Nutrition Plan", "Nutrient-dense plan with increased caloric intake and emphasis on iron, folate, calcium, and omega-3 fatty acids for fetal development."),
                ("Prenatal Nausea-Friendly Plan", "Gentle meal plan with small, frequent meals using bland and well-tolerated foods to manage first-trimester nausea while meeting nutritional needs."),
                ("Iron-Rich Prenatal Plan", "Plan focused on heme and non-heme iron sources paired with vitamin C to optimize absorption and prevent pregnancy-related anemia.")
            ],
            MacroTargets: (2200, 2400, 100, 130, 270, 310, 65, 80),
            RelevantGoalTypes: [GoalType.Weight, GoalType.Dietary],
            RelevantMetrics: [MetricType.Weight],
            GoalTitleTemplates:
            [
                "Gain 11-16 kg over pregnancy within guidelines",
                "Meet daily folate and iron targets through diet",
                "Establish consistent meal pattern of 5-6 small meals"
            ],
            FoodPoolTags: ["general", "iron-rich", "folate-rich", "prenatal"],
            AllergyPool:
            [
                ("Shellfish", AllergySeverity.Mild, AllergyType.Food),
                ("Dust mites", AllergySeverity.Mild, AllergyType.Environmental),
            ],
            MedicationPool:
            [
                ("Prenatal Multivitamin", null, "Once daily", "Prenatal nutrition"),
                ("Iron Supplement", "300 mg ferrous gluconate", "Once daily", "Iron supplementation"),
                ("Folic Acid", "1 mg", "Once daily", "Neural tube defect prevention"),
            ],
            ConditionPool:
            [
                ("Gestational diabetes", "O24.4", ConditionStatus.Active, "Monitoring blood glucose"),
                ("Iron-deficiency anemia", "D50.9", ConditionStatus.Active, "Supplementing with oral iron"),
            ],
            RestrictionPool: []),

        new ClientProfile(
            Tag: "ibs-fodmap",
            NoteTemplates:
            [
                "Client has completed the elimination phase of the low-FODMAP protocol. Symptom diary shows significant improvement in bloating and abdominal pain.",
                "Beginning structured reintroduction of fructans this week. Client will trial one serve of wheat bread and monitor symptoms for 48 hours.",
                "Garlic and onion remain confirmed triggers. Discussed alternatives such as garlic-infused oil and the green parts of spring onions for flavour.",
                "Client reports improved quality of life and confidence eating out. Provided a restaurant guide with low-FODMAP ordering strategies."
            ],
            MealPlanTemplates:
            [
                ("Low-FODMAP Elimination Plan", "Strict elimination-phase plan removing all high-FODMAP foods while maintaining nutritional adequacy and dietary variety."),
                ("FODMAP Reintroduction Plan", "Structured plan supporting systematic reintroduction of FODMAP groups one at a time with symptom monitoring guidance."),
                ("Maintenance Low-FODMAP Plan", "Personalized long-term plan incorporating known safe foods and avoiding confirmed triggers identified during reintroduction.")
            ],
            MacroTargets: (1700, 1900, 100, 120, 180, 220, 50, 65),
            RelevantGoalTypes: [GoalType.Dietary, GoalType.Custom],
            RelevantMetrics: [MetricType.Weight, MetricType.WaistCircumference],
            GoalTitleTemplates:
            [
                "Complete FODMAP elimination phase over 6 weeks",
                "Identify top 3 trigger foods through reintroduction",
                "Reduce symptom frequency to fewer than 2 episodes per week"
            ],
            FoodPoolTags: ["general", "low-fodmap"],
            AllergyPool:
            [
                ("Wheat", AllergySeverity.Moderate, AllergyType.Food),
                ("Lactose", AllergySeverity.Moderate, AllergyType.Food),
            ],
            MedicationPool:
            [
                ("Probiotics", null, "Once daily", "Gut health"),
                ("Peppermint Oil Capsules", "200 mg", "Three times daily before meals", "IBS symptom relief"),
            ],
            ConditionPool:
            [
                ("Irritable bowel syndrome", "K58", ConditionStatus.Active, "FODMAP management protocol"),
                ("Lactose intolerance", "E73.9", ConditionStatus.Active, null),
            ],
            RestrictionPool: [DietaryRestrictionType.GlutenFree, DietaryRestrictionType.DairyFree]),

        new ClientProfile(
            Tag: "cardiac-rehab",
            NoteTemplates:
            [
                "Client is 8 weeks post-cardiac event and progressing well with rehabilitation. Sodium intake has been reduced to under 2000 mg per day as recommended.",
                "Reviewed omega-3 fatty acid intake. Client is now consuming salmon twice weekly and using olive oil as primary cooking fat. Lipid panel recheck in 4 weeks.",
                "Blood pressure has improved since dietary changes. Client reports finding low-sodium cooking more enjoyable than expected with herb and spice alternatives.",
                "Discussed alcohol consumption guidelines. Client has reduced to 2 standard drinks per week maximum. Resting heart rate is trending downward."
            ],
            MealPlanTemplates:
            [
                ("Heart-Healthy Recovery Plan", "Low-sodium Mediterranean-style plan rich in omega-3 fatty acids, whole grains, and antioxidant-dense fruits and vegetables to support cardiac recovery."),
                ("DASH-Style Cardiac Plan", "DASH diet-aligned plan emphasizing potassium, calcium, and magnesium-rich foods while strictly limiting sodium and saturated fat."),
                ("Low-Sodium Meal Prep Plan", "Practical batch-cooking plan with pre-portioned low-sodium meals to simplify adherence during the rehabilitation period.")
            ],
            MacroTargets: (1800, 2000, 100, 130, 200, 240, 50, 65),
            RelevantGoalTypes: [GoalType.Weight, GoalType.Dietary],
            RelevantMetrics: [MetricType.Weight, MetricType.BloodPressureSystolic, MetricType.BloodPressureDiastolic, MetricType.RestingHeartRate],
            GoalTitleTemplates:
            [
                "Reduce resting blood pressure to below 130/80",
                "Maintain sodium intake under 2000 mg daily",
                "Achieve target LDL cholesterol through dietary changes"
            ],
            FoodPoolTags: ["general", "low-sodium", "mediterranean"],
            AllergyPool:
            [
                ("Aspirin", AllergySeverity.Moderate, AllergyType.Drug),
                ("Shellfish", AllergySeverity.Mild, AllergyType.Food),
            ],
            MedicationPool:
            [
                ("Atorvastatin", "40 mg", "Once daily at bedtime", "Dyslipidemia"),
                ("Metoprolol", "50 mg", "Twice daily", "Heart rate and blood pressure control"),
                ("Ramipril", "5 mg", "Once daily", "Blood pressure and cardiac remodelling"),
                ("ASA", "81 mg", "Once daily", "Antiplatelet therapy"),
            ],
            ConditionPool:
            [
                ("Coronary artery disease", "I25.1", ConditionStatus.Managed, "Post-stent placement"),
                ("Hypertension", "I10", ConditionStatus.Managed, null),
                ("Dyslipidemia", "E78.5", ConditionStatus.Active, "Target LDL < 2.0 mmol/L"),
            ],
            RestrictionPool: [DietaryRestrictionType.LowSodium]),

        new ClientProfile(
            Tag: "general-wellness",
            NoteTemplates:
            [
                "Client is looking to improve overall eating habits and establish a more consistent meal routine. No specific medical conditions or restrictions.",
                "Discussed the importance of balanced meals with protein, fibre, and healthy fats at each sitting. Client will focus on meal prepping lunches this week.",
                "Client reports feeling more energetic since increasing vegetable intake and reducing processed food consumption. Sleep quality has also improved.",
                "Reviewed grocery shopping strategies and label reading. Client is becoming more confident making healthier choices independently."
            ],
            MealPlanTemplates:
            [
                ("Balanced Whole Foods Plan", "Well-rounded plan built on whole grains, lean proteins, fruits, vegetables, and healthy fats to support overall health and energy levels."),
                ("Simple Healthy Eating Plan", "Approachable meal plan with easy-to-prepare meals designed for clients new to structured nutrition, focusing on building sustainable habits."),
                ("Weekly Meal Prep Starter Plan", "Practical plan organized around batch cooking with simple recipes to help establish a consistent and balanced eating routine.")
            ],
            MacroTargets: (1800, 2000, 90, 120, 200, 250, 55, 70),
            RelevantGoalTypes: [GoalType.Weight, GoalType.Dietary],
            RelevantMetrics: [MetricType.Weight],
            GoalTitleTemplates:
            [
                "Eat 5 servings of fruits and vegetables daily",
                "Establish a consistent 3-meal-per-day routine",
                "Reduce ultra-processed food intake by 50%"
            ],
            FoodPoolTags: ["general", "balanced"],
            AllergyPool:
            [
                ("Seasonal pollen", AllergySeverity.Mild, AllergyType.Environmental),
                ("Dust mites", AllergySeverity.Mild, AllergyType.Environmental),
            ],
            MedicationPool:
            [
                ("Multivitamin", null, "Once daily", "General nutrition"),
                ("Vitamin D", "1000 IU", "Once daily", "Bone health"),
            ],
            ConditionPool: [],
            RestrictionPool: []),

        new ClientProfile(
            Tag: "post-surgical",
            NoteTemplates:
            [
                "Client is 3 weeks post-surgery and transitioning from soft foods to regular textures. Protein intake remains the priority to support tissue healing.",
                "Reviewed supplement protocol — client is taking vitamin D, zinc, and a collagen peptide supplement as recommended by the surgical team.",
                "Appetite is gradually returning. Client managed three full meals yesterday for the first time since surgery. Encouraged continued small, frequent meals if needed.",
                "Wound healing is progressing well per the surgeon's last assessment. Client attributes improved recovery to the high-protein nutrition plan."
            ],
            MealPlanTemplates:
            [
                ("Post-Surgical Recovery Plan", "High-protein, nutrient-dense plan with anti-inflammatory foods to support wound healing, immune function, and recovery after surgery."),
                ("Soft Food Transition Plan", "Gentle progression plan moving from liquids through soft foods to regular textures while maintaining high protein and micronutrient targets."),
                ("High-Protein Healing Plan", "Protein-forward plan with emphasis on collagen-building nutrients including vitamin C, zinc, and amino acids to accelerate tissue repair.")
            ],
            MacroTargets: (2000, 2200, 130, 160, 200, 250, 55, 70),
            RelevantGoalTypes: [GoalType.Weight, GoalType.BodyComposition, GoalType.Custom],
            RelevantMetrics: [MetricType.Weight, MetricType.BodyFatPercentage],
            GoalTitleTemplates:
            [
                "Maintain weight within 2 kg of pre-surgical baseline",
                "Consume 130+ grams of protein daily during recovery",
                "Transition to full solid diet within 4 weeks post-surgery"
            ],
            FoodPoolTags: ["general", "high-protein", "anti-inflammatory"],
            AllergyPool:
            [
                ("Sulfa drugs", AllergySeverity.Severe, AllergyType.Drug),
                ("Codeine", AllergySeverity.Moderate, AllergyType.Drug),
            ],
            MedicationPool:
            [
                ("Acetaminophen", "500 mg", "Every 6 hours as needed", "Post-surgical pain management"),
                ("Iron Supplement", "300 mg ferrous gluconate", "Once daily", "Post-surgical anemia"),
                ("Collagen Peptides", "10 g", "Once daily", "Wound healing support"),
            ],
            ConditionPool:
            [
                ("Post-surgical recovery", null, ConditionStatus.Active, "Recovering from abdominal surgery"),
                ("Iron-deficiency anemia", "D50.9", ConditionStatus.Active, "Secondary to surgical blood loss"),
            ],
            RestrictionPool: []),
    };
}
