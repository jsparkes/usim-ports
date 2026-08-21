/* CADR QFASL disassembler
 */

#include <inttypes.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>
#include <strings.h>
#include <err.h>

#include "defmic.h"
#include "misc.h"
#include "ucode.h"
#include "unfasl.h"
#include "usim.h"

static int defmics_vector[1024];
static int defmics_size = NELEM(defmics);
static bool defmics_vector_setup;

static void
defmics_init(void)
{
	if (defmics_vector_setup == true)
		return;
	for (int i = 0; i < defmics_size; i++) {
		int index;
		if (defmics[i].name == NULL)
			break;
		index = defmics[i].value;
		defmics_vector[index] = i;
	}
	defmics_vector_setup = true;
}

#include "qcom.h"

char *
strdtp(int tag)
{
	switch (tag) {
	case DTP_TRAP: return "<dtp-trap>";
	case DTP_NULL: return "<dtp-null>";
	case DTP_FREE: return "<dtp-free>";
	case DTP_SYMBOL: return "<dtp-symbol>";
	case DTP_SYMBOL_HEADER: return "<dtp-symbol-header>";
	case DTP_FIX: return "<dtp-fix>";
	case DTP_EXTENDED_NUMBER: return "<dtp-extended-number>";
	case DTP_HEADER: return "<dtp-header>";
	case DTP_GC_FORWARD: return "<dtp-gc-forward>";
	case DTP_EXTERNAL_VALUE_CELL_POINTER: return "<dtp-external-value-cell-pointer>";
	case DTP_ONE_Q_FORWARD: return "<dtp-one-q-forward>";
	case DTP_HEADER_FORWARD: return "<dtp-header-forward>";
	case DTP_BODY_FORWARD: return "<dtp-body-forward>";
	case DTP_LOCATIVE: return "<dtp-locative>";
	case DTP_LIST: return "<dtp-list>";
	case DTP_U_ENTRY: return "<dtp-u-entry>";
	case DTP_FEF_POINTER: return "<dtp-fef-pointer>";
	case DTP_ARRAY_POINTER: return "<dtp-array-pointer>";
	case DTP_ARRAY_HEADER: return "<dtp-array-header>";
	case DTP_STACK_GROUP: return "<dtp-stack-group>";
	case DTP_CLOSURE: return "<dtp-closure>";
	case DTP_SMALL_FLONUM: return "<dtp-small-flonum>";
	case DTP_SELECT_METHOD: return "<dtp-select-method>";
	case DTP_INSTANCE: return "<dtp-instance>";
	case DTP_INSTANCE_HEADER: return "<dtp-instance-header>";
	case DTP_ENTITY: return "<dtp-entity>";
	case DTP_STACK_CLOSURE: return "<dtp-stack-closure>";
#if LISPM_SYSTEM >= 9800L		
	case DTP_SELF_REF_POINTER: return "<dtp-self-ref-pointer>";
	case DTP_CHARACTER: return "<dtp-character>";
#endif
	default: warnx("strdtp: unknown tag: 0%o\n", tag); return "dtp-???";
	}
}

#if LISPM_SYSTEM == 7800L
#include "unfasl78.c"
#elif LISPM_SYSTEM == 9800L
#include "unfasl98.c"
#elif LISPM_SYSTEM == 9900L
#include "unfasl99.c"
#elif LISPM_SYSTEM == 9999L
#include "unfasl300.c"
#endif
