import { TextInput } from './Settings';

interface Props {
  value: string;
  onUpdate: (field: string, value: string) => void;
  disabled?: boolean;
}

/**
 * Input component for the user's name.
 * - Works like other settings fields: updates parent state immediately on change.
 * - Limits to a maximum of 15 characters.
 */
export default function UserNameInput({ value, onUpdate, disabled }: Props) {
  return (
    <TextInput
      value={value ?? ''}
      onChange={(v: string) => onUpdate('userName', v.slice(0, 15))}
      placeholder="e.g. Hari Seldon"
      disabled={disabled}
      maxLength={15}
    />
  );
}
