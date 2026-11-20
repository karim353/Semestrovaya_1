'use strict';

const byId = id => document.getElementById(id);
const namePattern = /^[\p{L}][\p{L}\p{M} .'-]{1,59}$/u;
const emailPattern = /^[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,63}$/;
const cardCodePattern = /^[A-Z0-9]{2,8}-[0-9]{1,4}$/;
const cardNamePattern = /^[\p{L}\p{M}\p{N}\p{P}\p{S} ]{2,120}$/u;

async function json(url, options) {
  const response = await fetch(url, options);
  const payload = await response.json();
  if (!response.ok) throw new Error(payload.error || 'Не удалось выполнить запрос.');
  return payload;
}

const post = (url, values) => json(url, { method: 'POST', body: new URLSearchParams(values) });
const formData = form => new URLSearchParams(new FormData(form));

function status(element, message, error = false) {
  element.textContent = message;
  element.classList.toggle('is-error', error);
}

function submitForm(form, action) {
  if (!form) return;
  const message = form.querySelector('[data-status]');
  const button = form.querySelector('button[type="submit"]');
  form.addEventListener('submit', async event => {
    event.preventDefault();
    if (button.disabled) return;
    button.disabled = true;
    status(message, 'Отправляем…');
    try {
      await action(formData(form), message);
    } catch (error) {
      status(message, error.message, true);
    } finally {
      button.disabled = false;
    }
  });
}

submitForm(byId('loginForm'), async (values) => {
  await post('/api/auth/login', values);
  location.assign('/admin');
});

submitForm(byId('logoutForm'), async (values) => {
  await post('/api/auth/logout', values);
  location.assign('/auth/login');
});

const feedback = byId('feedbackForm');
if (feedback) {
  const input = byId('fbMessage');
  const count = () => { byId('messageCounter').textContent = input.value.length + ' / 1000'; };
  input.addEventListener('input', count);
  submitForm(feedback, async (values, message) => {
    if (!namePattern.test(values.get('name').trim())) throw new Error('Укажите имя из 2–60 букв.');
    if (!emailPattern.test(values.get('email').trim())) throw new Error('Укажите корректный e-mail.');
    if (values.get('message').trim().length < 5) throw new Error('Введите хотя бы 5 символов.');
    await post('/api/feedback', values);
    feedback.reset();
    count();
    status(message, 'Спасибо! Сообщение сохранено.');
  });
}

const cardForm = byId('cardForm');
if (cardForm) {
  const editor = byId('descriptionEditor');
  const save = byId('saveCard');
  const cancel = byId('cancelEdit');
  const adminMessage = byId('adminStatus');
  const count = () => { byId('editorCounter').textContent = editor.innerHTML.length + ' / 8000'; };
  editor.addEventListener('input', count);
  editor.addEventListener('drop', event => event.preventDefault());
  editor.addEventListener('paste', event => {
    event.preventDefault();
    document.execCommand('insertText', false, event.clipboardData.getData('text/plain'));
    count();
  });
  for (const button of cardForm.querySelectorAll('[data-format]')) {
    button.addEventListener('mousedown', event => event.preventDefault());
    button.addEventListener('click', () => {
      editor.focus();
      document.execCommand(button.dataset.format);
      count();
    });
  }

  function editMode(on) {
    byId('editorHeading').textContent = on ? 'Редактировать карту' : 'Добавить карту';
    save.textContent = on ? 'Сохранить изменения' : 'Добавить карту';
    cancel.hidden = !on;
  }
  cancel.addEventListener('click', () => {
    cardForm.reset();
    cardForm.elements.namedItem('id').value = '';
    editor.replaceChildren();
    count();
    editMode(false);
    status(cardForm.querySelector('[data-status]'), '');
  });

  submitForm(cardForm, async (values, message) => {
    const code = values.get('code').trim().toUpperCase();
    if (!cardCodePattern.test(code)) throw new Error('Введите код в формате LC-123 или DEMO-15.');
    if (!cardNamePattern.test(values.get('name').trim())) throw new Error('Проверьте название карты.');
    if (editor.innerHTML.length > 8000) throw new Error('Сократите описание до 8000 символов.');
    if (values.get('inkId') === values.get('secondInkId')) throw new Error('Цвета чернил должны отличаться.');
    values.set('code', code);
    values.set('descriptionHtml', editor.innerHTML);
    values.set('inkable', String(cardForm.elements.namedItem('inkable').checked));
    const action = values.get('id') ? 'update' : 'create';
    await post('/api/admin/cards/' + action, values);
    status(message, 'Карта сохранена.');
    const updatedUrl = new URL('/admin', location.origin);
    updatedUrl.searchParams.set('q', values.get('name').trim());
    location.assign(updatedUrl);
  });

  for (const button of document.querySelectorAll('[data-edit]')) {
    button.addEventListener('click', async () => {
      button.disabled = true;
      try {
        const card = await json('/api/admin/card/' + encodeURIComponent(button.dataset.edit));
        for (const [key, value] of Object.entries(card)) {
          const field = cardForm.elements.namedItem(key);
          if (!field) continue;
          if (field.type === 'checkbox') field.checked = Boolean(value);
          else field.value = value ?? '';
        }
        editor.innerHTML = card.descriptionHtml; // Очистка HTML выполнена на сервере.
        count();
        editMode(true);
        byId('cardName').focus();
      } catch (error) {
        status(adminMessage, error.message, true);
      } finally {
        button.disabled = false;
      }
    });
  }
  for (const button of document.querySelectorAll('[data-delete]')) {
    button.addEventListener('click', async () => {
      if (!confirm('Удалить карту из каталога?')) return;
      button.disabled = true;
      try {
        await post('/api/admin/cards/delete', { id: button.dataset.delete, csrf: cardForm.elements.namedItem('csrf').value });
        location.reload();
      } catch (error) {
        status(adminMessage, error.message, true);
        button.disabled = false;
      }
    });
  }
}
