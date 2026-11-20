BEGIN;

INSERT INTO inks (name) VALUES ('Amber'), ('Amethyst'), ('Emerald'), ('Ruby'), ('Sapphire'), ('Steel')
ON CONFLICT (name) DO NOTHING;
INSERT INTO card_sets (name) VALUES ('Demo Set') ON CONFLICT (name) DO NOTHING;
INSERT INTO rarities (name) VALUES ('Common'), ('Uncommon'), ('Rare'), ('Super Rare'), ('Legendary')
ON CONFLICT (name) DO NOTHING;

INSERT INTO cards (name, code, ink_id, set_id, rarity_id, cost, inkable, description_html)
SELECT demo.name, demo.code, inks.id, card_sets.id, rarities.id, demo.cost, demo.inkable, demo.description
FROM (VALUES
    ('Amber Guide', 'DEMO-1', 'Amber', 'Common', 1, true, '<p>Учебная карта с чернилами Amber.</p>'),
    ('Amethyst Scholar', 'DEMO-2', 'Amethyst', 'Uncommon', 2, false, '<p>Учебная карта для фильтра Non-Inkable.</p>'),
    ('Emerald Scout', 'DEMO-3', 'Emerald', 'Common', 3, true, '<p>Пример для каталога.</p>'),
    ('Ruby Champion', 'DEMO-4', 'Ruby', 'Rare', 4, true, '<p>Пример для детальной страницы.</p>'),
    ('Sapphire Navigator', 'DEMO-5', 'Sapphire', 'Rare', 5, false, '<p>Пример описания с <strong>выделением</strong>.</p>'),
    ('Steel Guardian', 'DEMO-6', 'Steel', 'Super Rare', 6, true, '<p>Учебная карта из стали.</p>'),
    ('Amber Pathfinder', 'DEMO-7', 'Amber', 'Uncommon', 7, false, '<p>Пример для фильтра по цене.</p>'),
    ('Ruby Wanderer', 'DEMO-8', 'Ruby', 'Rare', 8, true, '<p>Пример карты стоимостью 8.</p>'),
    ('Amethyst Oracle', 'DEMO-9', 'Amethyst', 'Legendary', 9, false, '<p>Пример для фильтра 9+.</p>'),
    ('Steel Colossus', 'DEMO-10', 'Steel', 'Legendary', 10, false, '<p>Вторая карта для фильтра 9+.</p>'),
    ('Steel Token', 'DEMO-11', 'Steel', 'Common', 0, false, '<p>Пример карты стоимостью 0.</p>')
) AS demo(name, code, ink, rarity, cost, inkable, description)
JOIN inks ON inks.name = demo.ink
JOIN rarities ON rarities.name = demo.rarity
JOIN card_sets ON card_sets.name = 'Demo Set'
ON CONFLICT (code) DO NOTHING;

COMMIT;
