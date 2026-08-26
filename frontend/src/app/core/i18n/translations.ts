/**
 * Every string the shell itself shows, in both languages.
 *
 * Only the shell lives here. Anything the server has an opinion about -- menu labels, account
 * names, and above all messages -- arrives already translated, because the server is where the
 * message catalogue is and duplicating it in the client would guarantee the two drift apart.
 */
export const TRANSLATIONS = {
  en: {
    'app.name': 'ASAP',
    'app.tagline': 'Enterprise Resource Planning',

    'auth.signIn': 'Sign in',
    'auth.signingIn': 'Signing in…',
    'auth.userName': 'User name',
    'auth.password': 'Password',
    'auth.signOut': 'Sign out',
    'auth.sessionEnded': 'Your session has ended. Please sign in again.',

    'shell.company': 'Company',
    'shell.branch': 'Branch',
    'shell.headOffice': 'Head office',
    'shell.language': 'العربية',
    'shell.menu': 'Menu',
    'shell.noAccess': 'You have no screens available in this company.',

    'home.welcome': 'Welcome',
    'home.youAreIn': 'You are working in',
    'home.permissions': 'permissions in this company',
    'home.openMenu': 'Choose a screen from the menu to begin.',

    'finance.accounts.title': 'Chart of accounts',
    'finance.accounts.no': 'Account',
    'finance.accounts.name': 'Name',
    'finance.accounts.type': 'Type',
    'finance.accounts.category': 'Category',
    'finance.accounts.balance': 'Balance',
    'finance.accounts.systemOnly': 'Posted by the system only',

    'finance.journal.title': 'General journal',
    'finance.journal.intro':
      'Enter the lines and post. Debits and credits must agree before anything reaches the ledger.',
    'finance.journal.documentNo': 'Document number',
    'finance.journal.description': 'Description',
    'finance.journal.account': 'Account',
    'finance.journal.debit': 'Debit',
    'finance.journal.credit': 'Credit',
    'finance.journal.addLine': 'Add line',
    'finance.journal.removeLine': 'Remove',
    'finance.journal.post': 'Post',
    'finance.journal.posting': 'Posting…',
    'finance.journal.totals': 'Totals',
    'finance.journal.difference': 'Difference',
    'finance.journal.balanced': 'Balanced',
    'finance.journal.outOfBalance': 'Out of balance',

    'finance.entries.title': 'Ledger entries',
    'finance.entries.date': 'Date',
    'finance.entries.transaction': 'Transaction',
    'finance.entries.document': 'Document',
    'finance.entries.source': 'Source',
    'finance.entries.reverse': 'Reverse',
    'finance.entries.reverseReason': 'Why is this being reversed?',
    'finance.entries.confirmReverse': 'Reverse transaction',
    'finance.entries.cancel': 'Cancel',
    'finance.entries.filterAccount': 'Filter by account',

    'finance.trialBalance.title': 'Trial balance',
    'finance.trialBalance.from': 'From',
    'finance.trialBalance.to': 'To',
    'finance.trialBalance.opening': 'Opening',
    'finance.trialBalance.closing': 'Closing',
    'finance.trialBalance.showAll': 'Include accounts with no activity',
    'finance.trialBalance.run': 'Run',
    'finance.trialBalance.balanced': 'Debits equal credits',
    'finance.trialBalance.notBalanced':
      'Debits do not equal credits. Every entry reaches the ledger through the posting engine, which refuses anything unbalanced, so this means something wrote it another way.',

    'common.loading': 'Loading…',
    'common.nothingHere': 'Nothing to show yet.',
    'common.retry': 'Try again',
    'common.close': 'Close',
    'common.whatToDo': 'What to do',
  },

  ar: {
    'app.name': 'أساب',
    'app.tagline': 'نظام تخطيط موارد المؤسسات',

    'auth.signIn': 'تسجيل الدخول',
    'auth.signingIn': 'جارٍ تسجيل الدخول…',
    'auth.userName': 'اسم المستخدم',
    'auth.password': 'كلمة المرور',
    'auth.signOut': 'تسجيل الخروج',
    'auth.sessionEnded': 'انتهت جلستك. الرجاء تسجيل الدخول مرة أخرى.',

    'shell.company': 'الشركة',
    'shell.branch': 'الفرع',
    'shell.headOffice': 'المركز الرئيسي',
    'shell.language': 'English',
    'shell.menu': 'القائمة',
    'shell.noAccess': 'لا توجد شاشات متاحة لك في هذه الشركة.',

    'home.welcome': 'مرحبًا',
    'home.youAreIn': 'أنت تعمل في',
    'home.permissions': 'صلاحية في هذه الشركة',
    'home.openMenu': 'اختر شاشة من القائمة للبدء.',

    'finance.accounts.title': 'شجرة الحسابات',
    'finance.accounts.no': 'الحساب',
    'finance.accounts.name': 'الاسم',
    'finance.accounts.type': 'النوع',
    'finance.accounts.category': 'التصنيف',
    'finance.accounts.balance': 'الرصيد',
    'finance.accounts.systemOnly': 'يُرحَّل بواسطة النظام فقط',

    'finance.journal.title': 'قيد اليومية',
    'finance.journal.intro':
      'أدخل السطور ثم رحّل. يجب أن يتساوى المدين والدائن قبل وصول أي شيء إلى دفتر الأستاذ.',
    'finance.journal.documentNo': 'رقم المستند',
    'finance.journal.description': 'البيان',
    'finance.journal.account': 'الحساب',
    'finance.journal.debit': 'مدين',
    'finance.journal.credit': 'دائن',
    'finance.journal.addLine': 'إضافة سطر',
    'finance.journal.removeLine': 'حذف',
    'finance.journal.post': 'ترحيل',
    'finance.journal.posting': 'جارٍ الترحيل…',
    'finance.journal.totals': 'الإجماليات',
    'finance.journal.difference': 'الفرق',
    'finance.journal.balanced': 'متوازن',
    'finance.journal.outOfBalance': 'غير متوازن',

    'finance.entries.title': 'قيود دفتر الأستاذ',
    'finance.entries.date': 'التاريخ',
    'finance.entries.transaction': 'الحركة',
    'finance.entries.document': 'المستند',
    'finance.entries.source': 'المصدر',
    'finance.entries.reverse': 'عكس',
    'finance.entries.reverseReason': 'ما سبب عكس هذا القيد؟',
    'finance.entries.confirmReverse': 'عكس الحركة',
    'finance.entries.cancel': 'إلغاء',
    'finance.entries.filterAccount': 'تصفية حسب الحساب',

    'finance.trialBalance.title': 'ميزان المراجعة',
    'finance.trialBalance.from': 'من',
    'finance.trialBalance.to': 'إلى',
    'finance.trialBalance.opening': 'رصيد افتتاحي',
    'finance.trialBalance.closing': 'رصيد ختامي',
    'finance.trialBalance.showAll': 'إظهار الحسابات بدون حركة',
    'finance.trialBalance.run': 'عرض',
    'finance.trialBalance.balanced': 'المدين يساوي الدائن',
    'finance.trialBalance.notBalanced':
      'المدين لا يساوي الدائن. كل قيد يصل إلى دفتر الأستاذ عبر محرك الترحيل الذي يرفض أي قيد غير متوازن، لذا فإن هذا يعني أن شيئًا ما كتب الدفتر بطريقة أخرى.',

    'common.loading': 'جارٍ التحميل…',
    'common.nothingHere': 'لا يوجد ما يُعرض بعد.',
    'common.retry': 'إعادة المحاولة',
    'common.close': 'إغلاق',
    'common.whatToDo': 'ما العمل',
  },
} as const;

/** The languages ASAP ships. */
export type AsapLanguage = keyof typeof TRANSLATIONS;

/** Every key the shell can translate. */
export type TranslationKey = keyof (typeof TRANSLATIONS)['en'];
