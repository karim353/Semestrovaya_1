BEGIN;

CREATE TABLE IF NOT EXISTS inks (
    id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name text NOT NULL UNIQUE,
    hex_color text NOT NULL DEFAULT '#999999' CHECK (hex_color ~ '^#[0-9a-fA-F]{6}$'),
    display_order integer NOT NULL DEFAULT 0 CHECK (display_order >= 0)
);

CREATE TABLE IF NOT EXISTS card_sets (
    id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name text NOT NULL UNIQUE,
    slug text NOT NULL DEFAULT '',
    description text NOT NULL DEFAULT ''
);

CREATE TABLE IF NOT EXISTS rarities (
    id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name text NOT NULL UNIQUE,
    label text NOT NULL DEFAULT '',
    rank integer NOT NULL DEFAULT 0 CHECK (rank >= 0)
);

CREATE TABLE IF NOT EXISTS cards (
    id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name text NOT NULL CHECK (char_length(name) BETWEEN 2 AND 120),
    code text NOT NULL UNIQUE CHECK (code ~ '^[A-Z0-9]{2,8}-[0-9]{1,4}$'),
    ink_id integer NOT NULL REFERENCES inks(id),
    second_ink_id integer REFERENCES inks(id) CHECK (second_ink_id <> ink_id),
    set_id integer NOT NULL REFERENCES card_sets(id),
    rarity_id integer NOT NULL REFERENCES rarities(id),
    cost integer NOT NULL CHECK (cost BETWEEN 0 AND 99),
    inkable boolean NOT NULL DEFAULT false,
    image_url text NOT NULL DEFAULT '' CHECK (char_length(image_url) <= 500),
    description_html text NOT NULL DEFAULT '',
    identifier text NOT NULL DEFAULT '',
    card_type text NOT NULL DEFAULT '',
    flavor text NOT NULL DEFAULT '',
    illustrator text NOT NULL DEFAULT ''
);

CREATE TABLE IF NOT EXISTS users (
    id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    login text NOT NULL UNIQUE CHECK (login ~ '^[a-z0-9_.-]{3,32}$'),
    password_salt bytea NOT NULL CHECK (octet_length(password_salt) = 16),
    password_hash bytea NOT NULL CHECK (octet_length(password_hash) = 32),
    role text NOT NULL CHECK (role IN ('admin', 'viewer'))
);

CREATE INDEX IF NOT EXISTS cards_ink_idx ON cards(ink_id);
CREATE INDEX IF NOT EXISTS cards_cost_idx ON cards(cost);
CREATE INDEX IF NOT EXISTS cards_order_idx ON cards(lower(name), id);

CREATE TABLE IF NOT EXISTS feedback (
    id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name text NOT NULL,
    email text NOT NULL,
    topic text NOT NULL,
    message text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

COMMIT;
