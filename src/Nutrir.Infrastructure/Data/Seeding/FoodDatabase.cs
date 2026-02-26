namespace Nutrir.Infrastructure.Data.Seeding;

/// <summary>
/// A single food entry with macronutrient data and categorical tags.
/// Macro consistency: (ProteinG * 4 + CarbsG * 4 + FatG * 9) should approximate CaloriesKcal within 10%.
/// </summary>
public record FoodEntry(
    string Name,
    decimal Quantity,
    string Unit,
    decimal CaloriesKcal,
    decimal ProteinG,
    decimal CarbsG,
    decimal FatG,
    string[] Tags,
    string? Notes = null);

/// <summary>
/// Static database of ~90 curated food items used for seed data generation.
/// Each entry has internally consistent macronutrient values.
/// </summary>
public static class FoodDatabase
{
    public static readonly IReadOnlyList<FoodEntry> All = new FoodEntry[]
    {
        // ============================================================
        // PROTEINS
        // ============================================================

        // Chicken breast (skinless, grilled): P31*4=124, C0*4=0, F3.6*9=32.4 => 156.4 vs 165 (3.5%)
        new("Chicken Breast (grilled)", 150m, "g", 165m, 31m, 0m, 3.6m,
            new[] { "general", "high-protein", "low-carb", "low-fodmap", "mediterranean", "low-sodium" }),

        // Salmon fillet (baked): P25*4=100, C0*4=0, F13*9=117 => 217 vs 220 (1.4%)
        new("Salmon Fillet (baked)", 150m, "g", 220m, 25m, 0m, 13m,
            new[] { "general", "high-protein", "mediterranean", "anti-inflammatory", "iron-rich" },
            "Rich in omega-3 fatty acids"),

        // Ground beef 90% lean: P26*4=104, C0*4=0, F11*9=99 => 203 vs 200 (1.5%)
        new("Ground Beef (90% lean)", 150m, "g", 200m, 26m, 0m, 11m,
            new[] { "general", "high-protein", "iron-rich", "energy-dense" }),

        // Turkey breast (roasted): P29*4=116, C0*4=0, F1*9=9 => 125 vs 125 (0%)
        new("Turkey Breast (roasted)", 120m, "g", 125m, 29m, 0m, 1m,
            new[] { "general", "high-protein", "low-calorie", "low-fodmap", "low-sodium" }),

        // Cod fillet (baked): P23*4=92, C0*4=0, F1*9=9 => 101 vs 100 (1%)
        new("Cod Fillet (baked)", 150m, "g", 100m, 23m, 0m, 1m,
            new[] { "general", "high-protein", "low-calorie", "low-fodmap", "mediterranean", "low-sodium" }),

        // Shrimp (cooked): P24*4=96, C0.2*4=0.8, F1.7*9=15.3 => 112.1 vs 112 (0.1%)
        new("Shrimp (cooked, peeled)", 120m, "g", 112m, 24m, 0.2m, 1.7m,
            new[] { "general", "high-protein", "low-calorie", "low-fodmap", "mediterranean", "low-sodium" }),

        // Eggs (2 large): P12*4=48, C1*4=4, F10*9=90 => 142 vs 143 (0.7%)
        new("Eggs (2 large, scrambled)", 100m, "g", 143m, 12m, 1m, 10m,
            new[] { "general", "high-protein", "balanced", "prenatal", "low-fodmap" }),

        // Egg whites (3 large): P11*4=44, C0.5*4=2, F0.2*9=1.8 => 47.8 vs 48 (0.4%)
        new("Egg Whites (3 large)", 100m, "g", 48m, 11m, 0.5m, 0.2m,
            new[] { "general", "high-protein", "low-calorie", "low-fodmap", "low-sodium" }),

        // Tofu (firm): P17*4=68, C3*4=12, F9*9=81 => 161 vs 160 (0.6%)
        new("Tofu (firm)", 200m, "g", 160m, 17m, 3m, 9m,
            new[] { "general", "high-protein", "low-fodmap", "anti-inflammatory", "low-sodium" }),

        // Tempeh: P20*4=80, C9*4=36, F11*9=99 => 215 vs 210 (2.4%)
        new("Tempeh", 100m, "g", 210m, 20m, 9m, 11m,
            new[] { "general", "high-protein", "iron-rich", "anti-inflammatory" }),

        // Pork tenderloin: P26*4=104, C0*4=0, F3*9=27 => 131 vs 130 (0.8%)
        new("Pork Tenderloin (roasted)", 120m, "g", 130m, 26m, 0m, 3m,
            new[] { "general", "high-protein", "low-fodmap" }),

        // Canned tuna (in water): P26*4=104, C0*4=0, F1*9=9 => 113 vs 110 (2.7%)
        new("Canned Tuna (in water)", 120m, "g", 110m, 26m, 0m, 1m,
            new[] { "general", "high-protein", "low-calorie", "low-fodmap", "low-sodium", "mediterranean" }),

        // ============================================================
        // LEGUMES
        // ============================================================

        // Lentils (cooked): P9*4=36, C20*4=80, F0.4*9=3.6 => 119.6 vs 116 (3.1%)
        new("Lentils (cooked)", 100m, "g", 116m, 9m, 20m, 0.4m,
            new[] { "general", "high-protein", "iron-rich", "folate-rich", "prenatal", "low-gi", "diabetic-friendly", "low-sodium" }),

        // Chickpeas (cooked): P9*4=36, C27*4=108, F2.6*9=23.4 => 167.4 vs 164 (2.1%)
        new("Chickpeas (cooked)", 100m, "g", 164m, 9m, 27m, 2.6m,
            new[] { "general", "high-protein", "iron-rich", "folate-rich", "mediterranean", "low-gi" }),

        // Black beans (cooked): P9*4=36, C24*4=96, F0.5*9=4.5 => 136.5 vs 132 (3.4%)
        new("Black Beans (cooked)", 100m, "g", 132m, 9m, 24m, 0.5m,
            new[] { "general", "high-protein", "iron-rich", "folate-rich", "low-gi", "diabetic-friendly", "low-sodium" }),

        // Edamame: P11*4=44, C9*4=36, F5*9=45 => 125 vs 122 (2.5%)
        new("Edamame (shelled)", 100m, "g", 122m, 11m, 9m, 5m,
            new[] { "general", "high-protein", "folate-rich", "low-gi", "anti-inflammatory" }),

        // ============================================================
        // GRAINS & STARCHES
        // ============================================================

        // Oats (dry): P13*4=52, C67*4=268, F7*9=63 => 383 vs 389 (1.5%)
        new("Rolled Oats (dry)", 100m, "g", 389m, 13m, 67m, 7m,
            new[] { "general", "high-carb", "energy-dense", "low-gi", "diabetic-friendly", "low-sodium", "balanced" }),

        // Cooked oatmeal: P5*4=20, C27*4=108, F3*9=27 => 155 vs 158 (1.9%)
        new("Oatmeal (cooked)", 250m, "g", 158m, 5m, 27m, 3m,
            new[] { "general", "high-carb", "low-gi", "diabetic-friendly", "balanced", "low-sodium" }),

        // Brown rice (cooked): P3*4=12, C23*4=92, F1*9=9 => 113 vs 112 (0.9%)
        new("Brown Rice (cooked)", 100m, "g", 112m, 3m, 23m, 1m,
            new[] { "general", "high-carb", "low-gi", "low-fodmap", "low-sodium", "mediterranean" }),

        // White rice (cooked): P3*4=12, C28*4=112, F0.3*9=2.7 => 126.7 vs 130 (2.5%)
        new("White Rice (cooked)", 100m, "g", 130m, 3m, 28m, 0.3m,
            new[] { "general", "high-carb", "low-fodmap", "low-sodium" }),

        // Quinoa (cooked): P4*4=16, C21*4=84, F2*9=18 => 118 vs 120 (1.7%)
        new("Quinoa (cooked)", 100m, "g", 120m, 4m, 21m, 2m,
            new[] { "general", "high-carb", "iron-rich", "folate-rich", "mediterranean", "low-gi", "low-sodium" }),

        // Whole wheat pasta (cooked): P5*4=20, C27*4=108, F1*9=9 => 137 vs 131 (4.6%)
        new("Whole Wheat Pasta (cooked)", 100m, "g", 131m, 5m, 27m, 1m,
            new[] { "general", "high-carb", "energy-dense", "low-gi", "low-sodium" }),

        // White pasta (cooked): P5*4=20, C31*4=124, F1.1*9=9.9 => 153.9 vs 158 (2.6%)
        new("Pasta (cooked)", 100m, "g", 158m, 5m, 31m, 1.1m,
            new[] { "general", "high-carb", "energy-dense" }),

        // Whole wheat bread: P4*4=16, C12*4=48, F1*9=9 => 73 vs 70 (4.3%)
        new("Whole Wheat Bread (1 slice)", 30m, "g", 70m, 4m, 12m, 1m,
            new[] { "general", "high-carb", "low-gi", "balanced" }),

        // Sweet potato (baked): P2*4=8, C21*4=84, F0.1*9=0.9 => 92.9 vs 90 (3.2%)
        new("Sweet Potato (baked)", 100m, "g", 90m, 2m, 21m, 0.1m,
            new[] { "general", "high-carb", "low-gi", "prenatal", "low-fodmap", "low-sodium", "anti-inflammatory" }),

        // Potato (baked): P2*4=8, C21*4=84, F0.1*9=0.9 => 92.9 vs 93 (0.1%)
        new("Potato (baked, with skin)", 100m, "g", 93m, 2m, 21m, 0.1m,
            new[] { "general", "high-carb", "low-fodmap", "low-sodium" }),

        // Corn tortilla: P1.5*4=6, C11*4=44, F0.7*9=6.3 => 56.3 vs 55 (2.4%)
        new("Corn Tortilla (1 small)", 25m, "g", 55m, 1.5m, 11m, 0.7m,
            new[] { "general", "high-carb", "low-fodmap" }),

        // Couscous (cooked): P4*4=16, C23*4=92, F0.2*9=1.8 => 109.8 vs 112 (2%)
        new("Couscous (cooked)", 100m, "g", 112m, 4m, 23m, 0.2m,
            new[] { "general", "high-carb", "mediterranean", "low-sodium" }),

        // ============================================================
        // DAIRY
        // ============================================================

        // Greek yogurt (plain, 2%): P20*4=80, C8*4=32, F3.5*9=31.5 => 143.5 vs 146 (1.7%)
        new("Greek Yogurt (plain, 2%)", 200m, "g", 146m, 20m, 8m, 3.5m,
            new[] { "general", "high-protein", "balanced", "low-gi", "prenatal", "low-fodmap" }),

        // Cottage cheese (2%): P14*4=56, C5*4=20, F2.5*9=22.5 => 98.5 vs 97 (1.5%)
        new("Cottage Cheese (2%)", 120m, "g", 97m, 14m, 5m, 2.5m,
            new[] { "general", "high-protein", "low-gi", "balanced" }),

        // Whole milk: P8*4=32, C12*4=48, F8*9=72 => 152 vs 150 (1.3%)
        new("Whole Milk", 250m, "mL", 150m, 8m, 12m, 8m,
            new[] { "general", "balanced", "prenatal" }),

        // Skim milk: P9*4=36, C13*4=52, F0.2*9=1.8 => 89.8 vs 90 (0.2%)
        new("Skim Milk", 250m, "mL", 90m, 9m, 13m, 0.2m,
            new[] { "general", "high-protein", "low-calorie", "low-gi" }),

        // Cheddar cheese: P7*4=28, C0.4*4=1.6, F9*9=81 => 110.6 vs 113 (2.1%)
        new("Cheddar Cheese", 30m, "g", 113m, 7m, 0.4m, 9m,
            new[] { "general", "high-protein", "energy-dense", "low-fodmap" }),

        // Mozzarella (part-skim): P7*4=28, C1*4=4, F5*9=45 => 77 vs 78 (1.3%)
        new("Mozzarella (part-skim)", 30m, "g", 78m, 7m, 1m, 5m,
            new[] { "general", "high-protein", "mediterranean", "low-fodmap" }),

        // Parmesan: P10*4=40, C1*4=4, F7*9=63 => 107 vs 110 (2.7%)
        new("Parmesan Cheese (grated)", 30m, "g", 110m, 10m, 1m, 7m,
            new[] { "general", "high-protein", "mediterranean", "energy-dense", "low-fodmap" }),

        // Plain yogurt (whole): P5*4=20, C8*4=32, F4*9=36 => 88 vs 90 (2.2%)
        new("Plain Yogurt (whole)", 150m, "g", 90m, 5m, 8m, 4m,
            new[] { "general", "balanced", "prenatal", "low-gi" }),

        // ============================================================
        // VEGETABLES
        // ============================================================

        // Broccoli (steamed): P3*4=12, C7*4=28, F0.4*9=3.6 => 43.6 vs 44 (0.9%)
        new("Broccoli (steamed)", 120m, "g", 44m, 3m, 7m, 0.4m,
            new[] { "general", "low-calorie", "folate-rich", "prenatal", "anti-inflammatory", "low-gi", "diabetic-friendly", "low-sodium", "low-fodmap" }),

        // Spinach (raw): P3*4=12, C4*4=16, F0.4*9=3.6 => 31.6 vs 32 (1.3%)
        new("Spinach (raw)", 100m, "g", 32m, 3m, 4m, 0.4m,
            new[] { "general", "low-calorie", "iron-rich", "folate-rich", "prenatal", "anti-inflammatory", "low-gi", "diabetic-friendly", "low-sodium" }),

        // Kale (raw): P4*4=16, C9*4=36, F0.9*9=8.1 => 60.1 vs 60 (0.2%)
        new("Kale (raw, chopped)", 100m, "g", 60m, 4m, 9m, 0.9m,
            new[] { "general", "low-calorie", "iron-rich", "folate-rich", "prenatal", "anti-inflammatory", "low-gi", "low-sodium" }),

        // Zucchini (sliced, cooked): P1.2*4=4.8, C3*4=12, F0.4*9=3.6 => 20.4 vs 20 (2%)
        new("Zucchini (cooked)", 120m, "g", 20m, 1.2m, 3m, 0.4m,
            new[] { "general", "low-calorie", "low-gi", "diabetic-friendly", "low-fodmap", "low-sodium", "mediterranean" }),

        // Bell peppers (raw): P1*4=4, C6*4=24, F0.3*9=2.7 => 30.7 vs 31 (1%)
        new("Bell Pepper (raw)", 120m, "g", 31m, 1m, 6m, 0.3m,
            new[] { "general", "low-calorie", "folate-rich", "low-gi", "diabetic-friendly", "low-fodmap", "low-sodium", "anti-inflammatory" }),

        // Carrots (raw): P1*4=4, C10*4=40, F0.2*9=1.8 => 45.8 vs 45 (1.8%)
        new("Carrots (raw)", 100m, "g", 45m, 1m, 10m, 0.2m,
            new[] { "general", "low-calorie", "low-gi", "prenatal", "low-fodmap", "low-sodium" }),

        // Tomato (raw): P1*4=4, C4*4=16, F0.2*9=1.8 => 21.8 vs 22 (0.9%)
        new("Tomato (raw, medium)", 120m, "g", 22m, 1m, 4m, 0.2m,
            new[] { "general", "low-calorie", "low-gi", "mediterranean", "anti-inflammatory", "low-sodium", "low-fodmap" }),

        // Cucumber (raw): P0.7*4=2.8, C4*4=16, F0.1*9=0.9 => 19.7 vs 20 (1.5%)
        new("Cucumber (raw)", 150m, "g", 20m, 0.7m, 4m, 0.1m,
            new[] { "general", "low-calorie", "low-gi", "diabetic-friendly", "low-fodmap", "low-sodium" }),

        // Asparagus (steamed): P2.5*4=10, C4*4=16, F0.2*9=1.8 => 27.8 vs 27 (3%)
        new("Asparagus (steamed)", 100m, "g", 27m, 2.5m, 4m, 0.2m,
            new[] { "general", "low-calorie", "folate-rich", "prenatal", "low-gi", "low-sodium", "anti-inflammatory" }),

        // Cauliflower (steamed): P2*4=8, C5*4=20, F0.3*9=2.7 => 30.7 vs 30 (2.3%)
        new("Cauliflower (steamed)", 120m, "g", 30m, 2m, 5m, 0.3m,
            new[] { "general", "low-calorie", "low-gi", "diabetic-friendly", "low-sodium", "low-fodmap" }),

        // Green beans (steamed): P2*4=8, C7*4=28, F0.2*9=1.8 => 37.8 vs 38 (0.5%)
        new("Green Beans (steamed)", 120m, "g", 38m, 2m, 7m, 0.2m,
            new[] { "general", "low-calorie", "low-gi", "low-fodmap", "low-sodium" }),

        // Brussels sprouts (roasted): P3*4=12, C9*4=36, F0.5*9=4.5 => 52.5 vs 52 (1%)
        new("Brussels Sprouts (roasted)", 100m, "g", 52m, 3m, 9m, 0.5m,
            new[] { "general", "low-calorie", "folate-rich", "anti-inflammatory", "low-gi", "low-sodium" }),

        // ============================================================
        // FRUITS
        // ============================================================

        // Banana (medium): P1.3*4=5.2, C27*4=108, F0.4*9=3.6 => 116.8 vs 112 (4.3%)
        new("Banana (medium)", 120m, "g", 112m, 1.3m, 27m, 0.4m,
            new[] { "general", "high-carb", "energy-dense", "low-fodmap", "low-sodium" }),

        // Blueberries: P0.7*4=2.8, C14*4=56, F0.3*9=2.7 => 61.5 vs 60 (2.5%)
        new("Blueberries", 100m, "g", 60m, 0.7m, 14m, 0.3m,
            new[] { "general", "low-calorie", "anti-inflammatory", "low-gi", "low-sodium", "prenatal" }),

        // Strawberries: P0.7*4=2.8, C8*4=32, F0.3*9=2.7 => 37.5 vs 36 (4.2%)
        new("Strawberries", 120m, "g", 36m, 0.7m, 8m, 0.3m,
            new[] { "general", "low-calorie", "anti-inflammatory", "low-gi", "diabetic-friendly", "low-sodium", "low-fodmap" }),

        // Mixed berries: P1*4=4, C12*4=48, F0.4*9=3.6 => 55.6 vs 55 (1.1%)
        new("Mixed Berries", 120m, "g", 55m, 1m, 12m, 0.4m,
            new[] { "general", "low-calorie", "anti-inflammatory", "low-gi", "low-sodium" }),

        // Apple (medium): P0.5*4=2, C25*4=100, F0.3*9=2.7 => 104.7 vs 104 (0.7%)
        new("Apple (medium, with skin)", 180m, "g", 104m, 0.5m, 25m, 0.3m,
            new[] { "general", "low-calorie", "low-gi", "low-sodium", "low-fodmap" }),

        // Orange (medium): P1*4=4, C15*4=60, F0.2*9=1.8 => 65.8 vs 65 (1.2%)
        new("Orange (medium)", 140m, "g", 65m, 1m, 15m, 0.2m,
            new[] { "general", "low-calorie", "folate-rich", "prenatal", "low-gi", "low-sodium" }),

        // Grapes: P0.7*4=2.8, C18*4=72, F0.2*9=1.8 => 76.6 vs 75 (2.1%)
        new("Grapes (seedless)", 120m, "g", 75m, 0.7m, 18m, 0.2m,
            new[] { "general", "high-carb", "low-sodium" }),

        // Mango (sliced): P1*4=4, C25*4=100, F0.6*9=5.4 => 109.4 vs 108 (1.3%)
        new("Mango (sliced)", 165m, "g", 108m, 1m, 25m, 0.6m,
            new[] { "general", "high-carb", "folate-rich", "low-sodium" }),

        // ============================================================
        // HEALTHY FATS & NUTS
        // ============================================================

        // Olive oil: P0*4=0, C0*4=0, F14*9=126 => 126 vs 124 (1.6%)
        new("Olive Oil (extra virgin)", 15m, "mL", 124m, 0m, 0m, 14m,
            new[] { "general", "energy-dense", "mediterranean", "anti-inflammatory", "low-fodmap", "low-sodium" }),

        // Avocado (half): P2*4=8, C9*4=36, F15*9=135 => 179 vs 180 (0.6%)
        new("Avocado (half)", 100m, "g", 180m, 2m, 9m, 15m,
            new[] { "general", "energy-dense", "anti-inflammatory", "low-gi", "mediterranean", "low-sodium", "low-fodmap" }),

        // Almonds: P6*4=24, C6*4=24, F14*9=126 => 174 vs 170 (2.4%)
        new("Almonds (raw)", 30m, "g", 170m, 6m, 6m, 14m,
            new[] { "general", "energy-dense", "low-gi", "diabetic-friendly", "anti-inflammatory", "low-fodmap", "low-sodium" }),

        // Walnuts: P4*4=16, C4*4=16, F18*9=162 => 194 vs 196 (1%)
        new("Walnuts (raw)", 30m, "g", 196m, 4m, 4m, 18m,
            new[] { "general", "energy-dense", "anti-inflammatory", "mediterranean", "low-sodium", "low-fodmap" }),

        // Peanut butter: P8*4=32, C6*4=24, F16*9=144 => 200 vs 190 (5.3%)
        new("Peanut Butter (natural)", 32m, "g", 190m, 8m, 6m, 16m,
            new[] { "general", "energy-dense", "high-protein", "balanced" }),

        // Chia seeds: P5*4=20, C12*4=48, F9*9=81 => 149 vs 138 (8%)
        new("Chia Seeds", 30m, "g", 138m, 5m, 12m, 9m,
            new[] { "general", "energy-dense", "anti-inflammatory", "iron-rich", "low-gi", "low-sodium" },
            "High in fiber and omega-3"),

        // Flaxseeds (ground): P5*4=20, C8*4=32, F12*9=108 => 160 vs 160 (0%)
        new("Flaxseeds (ground)", 30m, "g", 160m, 5m, 8m, 12m,
            new[] { "general", "energy-dense", "anti-inflammatory", "iron-rich", "low-gi", "low-sodium" },
            "High in fiber and omega-3"),

        // Pumpkin seeds: P9*4=36, C4*4=16, F14*9=126 => 178 vs 170 (4.7%)
        new("Pumpkin Seeds (raw)", 30m, "g", 170m, 9m, 4m, 14m,
            new[] { "general", "energy-dense", "iron-rich", "low-gi", "low-sodium" }),

        // Cashews: P5*4=20, C9*4=36, F13*9=117 => 173 vs 170 (1.8%)
        new("Cashews (raw)", 30m, "g", 170m, 5m, 9m, 13m,
            new[] { "general", "energy-dense", "iron-rich" }),

        // Tahini: P5*4=20, C3*4=12, F14*9=126 => 158 vs 160 (1.3%)
        new("Tahini", 30m, "g", 160m, 5m, 3m, 14m,
            new[] { "general", "energy-dense", "iron-rich", "mediterranean", "low-gi" }),

        // ============================================================
        // PREPARED / COMPOSITE FOODS
        // ============================================================

        // Protein smoothie: P30*4=120, C35*4=140, F5*9=45 => 305 vs 310 (1.6%)
        new("Protein Smoothie (banana, protein powder, milk)", 400m, "mL", 310m, 30m, 35m, 5m,
            new[] { "general", "high-protein", "energy-dense", "balanced" }),

        // Turkey wrap: P25*4=100, C30*4=120, F8*9=72 => 292 vs 290 (0.7%)
        new("Turkey Wrap (whole wheat)", 200m, "g", 290m, 25m, 30m, 8m,
            new[] { "general", "high-protein", "balanced" }),

        // Chicken stir-fry with vegetables: P28*4=112, C18*4=72, F10*9=90 => 274 vs 280 (2.1%)
        new("Chicken Stir-Fry with Vegetables", 300m, "g", 280m, 28m, 18m, 10m,
            new[] { "general", "high-protein", "balanced", "low-gi" }),

        // Grilled chicken salad: P25*4=100, C10*4=40, F12*9=108 => 248 vs 250 (0.8%)
        new("Grilled Chicken Salad", 300m, "g", 250m, 25m, 10m, 12m,
            new[] { "general", "high-protein", "low-calorie", "balanced", "mediterranean", "low-gi", "diabetic-friendly" }),

        // Hummus: P4*4=16, C9*4=36, F6*9=54 => 106 vs 105 (1%)
        new("Hummus", 60m, "g", 105m, 4m, 9m, 6m,
            new[] { "general", "mediterranean", "low-gi", "low-sodium" }),

        // Salmon with quinoa and vegetables: P32*4=128, C28*4=112, F14*9=126 => 366 vs 370 (1.1%)
        new("Salmon with Quinoa and Vegetables", 350m, "g", 370m, 32m, 28m, 14m,
            new[] { "general", "high-protein", "balanced", "mediterranean", "anti-inflammatory" }),

        // Overnight oats: P12*4=48, C40*4=160, F8*9=72 => 280 vs 280 (0%)
        new("Overnight Oats (with yogurt and berries)", 300m, "g", 280m, 12m, 40m, 8m,
            new[] { "general", "balanced", "low-gi", "prenatal" }),

        // Tuna salad: P22*4=88, C5*4=20, F10*9=90 => 198 vs 200 (1%)
        new("Tuna Salad", 150m, "g", 200m, 22m, 5m, 10m,
            new[] { "general", "high-protein", "balanced", "mediterranean" }),

        // Vegetable soup: P4*4=16, C15*4=60, F2*9=18 => 94 vs 95 (1.1%)
        new("Vegetable Soup", 250m, "mL", 95m, 4m, 15m, 2m,
            new[] { "general", "low-calorie", "low-gi", "diabetic-friendly", "low-sodium", "anti-inflammatory" }),

        // Lentil soup: P9*4=36, C20*4=80, F3*9=27 => 143 vs 145 (1.4%)
        new("Lentil Soup", 250m, "mL", 145m, 9m, 20m, 3m,
            new[] { "general", "iron-rich", "folate-rich", "low-gi", "diabetic-friendly", "low-sodium" }),

        // Bean burrito bowl: P18*4=72, C45*4=180, F10*9=90 => 342 vs 340 (0.6%)
        new("Bean Burrito Bowl (rice, beans, vegetables)", 350m, "g", 340m, 18m, 45m, 10m,
            new[] { "general", "high-carb", "balanced", "iron-rich" }),

        // Caprese salad: P10*4=40, C5*4=20, F14*9=126 => 186 vs 190 (2.1%)
        new("Caprese Salad", 200m, "g", 190m, 10m, 5m, 14m,
            new[] { "general", "mediterranean", "low-gi", "low-sodium" }),

        // Egg and avocado toast: P12*4=48, C20*4=80, F16*9=144 => 272 vs 270 (0.7%)
        new("Egg and Avocado Toast (whole wheat)", 180m, "g", 270m, 12m, 20m, 16m,
            new[] { "general", "balanced", "energy-dense", "prenatal" }),

        // ============================================================
        // SPECIALTY / DIETARY ITEMS
        // ============================================================

        // Turmeric golden milk: P6*4=24, C15*4=60, F5*9=45 => 129 vs 130 (0.8%)
        new("Turmeric Golden Milk", 250m, "mL", 130m, 6m, 15m, 5m,
            new[] { "general", "anti-inflammatory", "low-gi" },
            "Anti-inflammatory spiced milk beverage"),

        // Sardines (canned in olive oil): P23*4=92, C0*4=0, F11*9=99 => 191 vs 190 (0.5%)
        new("Sardines (canned in olive oil)", 100m, "g", 190m, 23m, 0m, 11m,
            new[] { "general", "high-protein", "iron-rich", "mediterranean", "anti-inflammatory", "energy-dense" },
            "Rich in omega-3 and calcium"),

        // Rice cakes: P1*4=4, C8*4=32, F0.3*9=2.7 => 38.7 vs 37 (4.6%)
        new("Rice Cake (plain)", 10m, "g", 37m, 1m, 8m, 0.3m,
            new[] { "general", "low-calorie", "low-fodmap", "low-sodium" }),

        // Dark chocolate (70%): P2*4=8, C10*4=40, F10*9=90 => 138 vs 140 (1.4%)
        new("Dark Chocolate (70% cacao)", 30m, "g", 140m, 2m, 10m, 10m,
            new[] { "general", "energy-dense", "iron-rich", "anti-inflammatory" }),

        // Coconut oil: P0*4=0, C0*4=0, F14*9=126 => 126 vs 126 (0%)
        new("Coconut Oil", 15m, "mL", 126m, 0m, 0m, 14m,
            new[] { "general", "energy-dense", "low-fodmap", "low-sodium" }),

        // Fortified cereal: P3*4=12, C24*4=96, F1*9=9 => 117 vs 120 (2.5%)
        new("Fortified Breakfast Cereal", 30m, "g", 120m, 3m, 24m, 1m,
            new[] { "general", "high-carb", "iron-rich", "folate-rich", "prenatal" }),

        // Soy milk (unsweetened): P7*4=28, C4*4=16, F4*9=36 => 80 vs 80 (0%)
        new("Soy Milk (unsweetened)", 250m, "mL", 80m, 7m, 4m, 4m,
            new[] { "general", "high-protein", "low-gi", "diabetic-friendly", "anti-inflammatory", "low-sodium" }),

        // Almond milk (unsweetened): P1*4=4, C1*4=4, F3*9=27 => 35 vs 35 (0%)
        new("Almond Milk (unsweetened)", 250m, "mL", 35m, 1m, 1m, 3m,
            new[] { "general", "low-calorie", "low-gi", "diabetic-friendly", "low-fodmap", "low-sodium" }),

        // Dried apricots: P1*4=4, C17*4=68, F0.2*9=1.8 => 73.8 vs 72 (2.5%)
        new("Dried Apricots", 30m, "g", 72m, 1m, 17m, 0.2m,
            new[] { "general", "high-carb", "iron-rich", "prenatal" }),

        // Hemp hearts: P10*4=40, C2*4=8, F15*9=135 => 183 vs 180 (1.7%)
        new("Hemp Hearts", 30m, "g", 180m, 10m, 2m, 15m,
            new[] { "general", "energy-dense", "high-protein", "anti-inflammatory", "iron-rich", "low-gi", "low-sodium" }),

        // Baked chicken thigh: P22*4=88, C0*4=0, F10*9=90 => 178 vs 180 (1.1%)
        new("Chicken Thigh (baked, skinless)", 120m, "g", 180m, 22m, 0m, 10m,
            new[] { "general", "high-protein", "iron-rich", "low-fodmap" }),

        // Basmati rice (cooked): P3*4=12, C25*4=100, F0.3*9=2.7 => 114.7 vs 116 (1.1%)
        new("Basmati Rice (cooked)", 100m, "g", 116m, 3m, 25m, 0.3m,
            new[] { "general", "high-carb", "low-gi", "low-fodmap", "low-sodium" }),

        // Mixed green salad (no dressing): P2*4=8, C4*4=16, F0.3*9=2.7 => 26.7 vs 25 (6.8%)
        new("Mixed Green Salad (no dressing)", 100m, "g", 25m, 2m, 4m, 0.3m,
            new[] { "general", "low-calorie", "low-gi", "diabetic-friendly", "low-sodium", "low-fodmap", "mediterranean" }),

        // Balsamic vinaigrette: P0*4=0, C3*4=12, F5*9=45 => 57 vs 55 (3.6%)
        new("Balsamic Vinaigrette", 15m, "mL", 55m, 0m, 3m, 5m,
            new[] { "general", "mediterranean", "low-fodmap" }),

        // Ground turkey: P27*4=108, C0*4=0, F8*9=72 => 180 vs 180 (0%)
        new("Ground Turkey (93% lean)", 150m, "g", 180m, 27m, 0m, 8m,
            new[] { "general", "high-protein", "balanced", "low-fodmap" }),
    };

    /// <summary>
    /// Returns all food entries that have the specified tag.
    /// </summary>
    public static IReadOnlyList<FoodEntry> GetByTag(string tag)
    {
        return All.Where(f => f.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>
    /// Returns all food entries that match ANY of the provided tags.
    /// </summary>
    public static IReadOnlyList<FoodEntry> GetByTags(string[] tags)
    {
        return All.Where(f => f.Tags.Any(t => tags.Contains(t, StringComparer.OrdinalIgnoreCase))).ToList();
    }
}
